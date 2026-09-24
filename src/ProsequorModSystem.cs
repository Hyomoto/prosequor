using HarmonyLib;
using Newtonsoft.Json;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Client;
using Prosequor.Client.CatEyes;
using Prosequor.Commands;
using Prosequor.Data;
using Prosequor.Inventory;
using Prosequor.Network;
using Prosequor.Player;
using Prosequor.Progress;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor;

/// <summary>Entry point: registry, progress behavior, commands, C-menu Skills tab.</summary>
public class ProsequorModSystem : ModSystem
{
    public const string ModId = "prosequor";

    /// <summary>
    /// Per-side instance. Singleplayer runs a client and a server mod system in one process,
    /// so a single static would let one side overwrite the other.
    /// </summary>
    public static ProsequorModSystem? For(ICoreAPI? api) =>
        api?.ModLoader?.GetModSystem<ProsequorModSystem>();

    public SkillRegistry Registry { get; } = new();
    public AttributeStatRegistry AttributeStats { get; } = new();
    public TraitAttributeRegistry TraitAttributes { get; } = new();
    public LevelUpRegistry LevelUps { get; } = new();
    public PhaseRefreshRegistry PhaseRefresh { get; } = new();
    public AttributeEffectService? AttributeEffects { get; private set; }
    public ProgressEventBus ProgressEvents { get; } = new();
    public XpRuleRegistry XpRules { get; } = new();
    public ActivityWrapperRegistry ActivityWrappers { get; } = new();
    public HookRegistry Hooks { get; } = new();
    public AbilityActionRegistry Actions { get; }
    public CollectionRegistry Collections { get; } = new();
    public CollectibleVariantTable VariantTable { get; } = new();
    public OutputPoolRegistry OutputPools { get; } = new();
    public AffixListRegistry AffixLists { get; } = new();
    public AbilityPipeline? Pipeline { get; private set; }
    public XpActionDispatcher? XpDispatcher { get; private set; }
    public CraftXpAdapter? CraftXp { get; private set; }
    public ClayFormXpAdapter? ClayFormXp { get; private set; }
    public ActivityWatchService? ActivityWatch { get; private set; }
    public FatherXp? FatherXp { get; private set; }
    public ProgressPark? ProgressPark { get; private set; }
    public ClayFormingRecipeCatalog? ClayFormingRecipes { get; private set; }
    public SmithingRecipeCatalog? SmithingRecipes { get; private set; }
    public BlockBreakHardnessCatalog? BlockBreakHardness { get; private set; }
    public AnimalWeightCatalog? AnimalWeight { get; private set; }
    public CropLifetimeCatalog? CropLifetime { get; private set; }
    public ProgressNetwork Network { get; } = new();
    public ContentFingerprint? Fingerprint { get; private set; }

    // Patches are process-wide, so client and server share one set and the last side out removes it.
    static readonly object patchLock = new();
    static Harmony? sharedHarmony;
    static int patchUsers;

    ICoreServerAPI? sapi;
    ICoreClientAPI? capi;
    CharacterSkillsTab? skillsTab;
    CharacterStatsPanel? statsPanel;
    CatEyesController? catEyes;
    LevelUpHudController? levelUpHud;
    HudElementLevelUp? levelUpHudElement;
    SkillWaitingHudController? skillWaitingHud;
    HudElementSkillWaiting? skillWaitingHudElement;
    AnimalAlertOverlayRenderer? animalAlertOverlay;
    Action<LevelUpHudPacket>? levelUpHudHandler;
    Action? skillWaitingDumpHandler;
    Action<ContentFingerprintMismatchPacket>? contentMismatchHandler;
    BlockBreakXpAdapter? blockBreakXpAdapter;
    FishingCatchXpAdapter? fishingCatchXpAdapter;
    CraftXpAdapter? craftXpAdapter;
    ClayFormXpAdapter? clayFormXpAdapter;
    long activityWatchListenerId;
    long progressFlushListenerId;
    long animalAlertListenerId;
    bool holdsPatches;

    public ProsequorModSystem()
    {
        Actions = new AbilityActionRegistry(Hooks);
        // Effort XP uses Effort.Emit + ActivityWatchService materialization (no shipped wrappers).
    }

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    /// <summary>
    /// Whether a companion mod may run with this Prosequor. Unknown ids are allowed.
    /// Companions call this from their own <c>StartPre</c>; Prosequor's watchdog only logs.
    /// </summary>
    public SupportAnswer QuerySupport(string? modId, string? version) =>
        CompanionSupport.QuerySupport(modId, version);

    public override void StartPre(ICoreAPI api)
    {
        CompanionSupport.WatchdogLog(api);
    }

    public override void Start(ICoreAPI api)
    {
        ProgressEvents.SetLogger(api.Logger);
        api.RegisterEntityBehaviorClass(EntityBehaviorProgress.Code, typeof(EntityBehaviorProgress));
        api.RegisterBlockEntityClass(
            BlockEntityProsequorPedigree.ClassName,
            typeof(BlockEntityProsequorPedigree));
        api.RegisterCollectibleBehaviorClass(
            MetalBitAnvilWorkableBehavior.ClassName,
            typeof(MetalBitAnvilWorkableBehavior));
        ProsequorCarryInventory.RegisterClass(api);

        AbilityBootstrap.RegisterBuiltIns(Hooks, Actions, AffixLists);
        PhaseRefreshRegistry.RegisterBuiltIns(PhaseRefresh);
        AcquirePatches();

        if (api.Side == EnumAppSide.Client)
        {
            LevelUpAudio.ReadGain(api);
        }

        api.Logger.Notification("[{0}] Loaded.", ModId);
    }

    void AcquirePatches()
    {
        lock (patchLock)
        {
            if (holdsPatches)
            {
                return;
            }

            if (sharedHarmony == null)
            {
                sharedHarmony = new Harmony(ModId);
                sharedHarmony.PatchAll(typeof(ProsequorModSystem).Assembly);
                CraftMutateOutputAttributePatches.TryPatchOptionalReaders(sharedHarmony);
                KilnFireXpPatches.TryPatchOptionalIgniters(sharedHarmony);
                CementationAbilityPatches.TryPatchOptionalIgniters(sharedHarmony);
                PlayerModelLibCompat.TryPatch(sharedHarmony);
                KnapsterCompat.TryPatch(sharedHarmony);
                ProsequorStackPedigree.EnsurePedigreeIgnoredForMerge();
            }

            holdsPatches = true;
            patchUsers++;
        }
    }

    void ReleasePatches()
    {
        lock (patchLock)
        {
            if (!holdsPatches)
            {
                return;
            }

            holdsPatches = false;
            patchUsers--;
            if (patchUsers <= 0)
            {
                sharedHarmony?.UnpatchAll(ModId);
                sharedHarmony = null;
                patchUsers = 0;
                BlockInteractSpeedPatch.ResetOverridePatchGate();
            }
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        // Collection keys (including pools) must exist before skill compile validates <> refs.
        // Affix lists must load before skill compile so add-affix { list, item } can resolve.
        Collections.LoadFromAssets(api);
        OutputPools.LoadFromAssets(api, Collections.Index);
        AffixLists.LoadFromAssets(api);
        Registry.LoadFromAssets(api, Hooks, Actions, Collections.Index);
        AttributeStats.LoadFromAssets(api, Hooks, Actions, Collections.Index);
        TraitAttributes.LoadFromAssets(api);
        LevelUps.LoadFromAssets(api);
        AttributeEffects = new AttributeEffectService(AttributeStats.EffectIndex, PhaseRefresh);
        Pipeline = new AbilityPipeline(Actions, Registry, AttributeStats);
        XpRules.LoadFromSkills(api, Registry);

        Fingerprint = ContentFingerprint.Compute(
            Registry,
            AttributeStats,
            Collections.Index,
            OutputPools,
            AffixLists,
            TraitAttributes,
            LevelUps);
        Network.SetFingerprint(Fingerprint);
        api.Logger.Notification(
            "[{0}] Content fingerprint v{1} {2}",
            ModId,
            Fingerprint.Version,
            Fingerprint.Hash);

        if (api is ICoreClientAPI clientApi)
        {
            ProsequorCarryInventory.RegisterSlotBackgroundIcon(clientApi);
        }

        MetalBitAnvilWorkableBehavior.AttachAll(api);

        lock (patchLock)
        {
            if (sharedHarmony != null)
            {
                BlockInteractSpeedPatch.PatchDeclaredOverrides(sharedHarmony);
            }
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        Network.StartServer(api);
        XpDispatcher = new XpActionDispatcher(api, XpRules);
        blockBreakXpAdapter = new BlockBreakXpAdapter(api, XpDispatcher);
        blockBreakXpAdapter.Start();
        fishingCatchXpAdapter = new FishingCatchXpAdapter(api, XpDispatcher);
        fishingCatchXpAdapter.Start();
        craftXpAdapter = new CraftXpAdapter(api, XpDispatcher);
        craftXpAdapter.Start();
        CraftXp = craftXpAdapter;
        clayFormXpAdapter = new ClayFormXpAdapter(api, XpDispatcher);
        clayFormXpAdapter.Start();
        ClayFormXp = clayFormXpAdapter;

        Effort.RegisterPoll(Effort.PollIdMount, EffortMountEmitter.TryPoll);
        Effort.RegisterPoll(Effort.PollIdFishing, EffortFishingEmitter.TryPoll);
        Effort.RegisterPoll(Effort.PollIdFoot, EffortFootEmitter.TryPoll);
        Effort.RegisterPoll(Effort.PollIdTemporalDrain, EffortTemporalDrainEmitter.TryPoll);

        // World items/blocks exist by GameReady; expand collection membership then.
        api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, () =>
        {
            Collections.RebuildMembership(api);
            OutputPools.ApplyToIndex(api, Collections.Index);

            VariantTable.Clear();
            DecorativePotteryVariants.Populate(api, VariantTable);

            ClayFormingRecipes = ClayFormingRecipeCatalog.Build(api);
            ClayFormingRecipes.FillClayFormedCollection(Collections.Index);
            api.Logger.Notification(
                "[prosequor] Clayforming recipe catalog: {0} recipes, voxels/unit {1}–{2}, clay-formed codes {3}.",
                ClayFormingRecipes.RecipeCount,
                ClayFormingRecipes.MinVoxelsPerUnit,
                ClayFormingRecipes.MaxVoxelsPerUnit,
                ClayFormingRecipes.OutputCodes.Count);

            SmithingRecipes = SmithingRecipeCatalog.Build(api);
            SmithingRecipes.FillSmithingFormedCollection(Collections.Index);
            Collections.Index.ApplyExcludes(msg => api.Logger.Warning(msg));
            Collections.Index.MaterializeUnions(msg => api.Logger.Warning(msg));
            api.Logger.Notification(
                "[prosequor] Smithing recipe catalog: {0} recipes, smithing-formed codes {1}.",
                SmithingRecipes.RecipeCount,
                SmithingRecipes.OutputCodes.Count);

            // Bake after all membership sources (patterns, pools, clay-formed, smithing-formed) are filled.
            TagCriterion.BindAll(Registry, AttributeStats, Collections.Index);

            BlockBreakHardness = BlockBreakHardnessCatalog.Build(api);
            LogHardnessDomain(api, BlockBreakHardnessCatalog.DomainDig);
            LogHardnessDomain(api, BlockBreakHardnessCatalog.DomainMine);
            LogHardnessDomain(api, BlockBreakHardnessCatalog.DomainChop);

            AnimalWeight = AnimalWeightCatalog.Build(api);
            if (AnimalWeight.TryGet() is AnimalWeightCatalog.Range animalSpan)
            {
                api.Logger.Notification(
                    "[prosequor] Animal weight catalog: {0} entities, weight {1:0.###}–{2:0.###}.",
                    animalSpan.EntityCount,
                    animalSpan.Min,
                    animalSpan.Max);
            }

            CropLifetime = CropLifetimeCatalog.Build(api);
            if (CropLifetime.TryGet() is CropLifetimeCatalog.Range cropSpan)
            {
                api.Logger.Notification(
                    "[prosequor] Crop lifetime catalog: {0} blocks, growth days {1:0.###}–{2:0.###}.",
                    cropSpan.BlockCount,
                    cropSpan.Min,
                    cropSpan.Max);
            }
            else
            {
                api.Logger.Notification("[prosequor] Crop lifetime catalog: none.");
            }
        });

        void LogHardnessDomain(ICoreAPI api, string domain)
        {
            BlockBreakHardnessCatalog.DomainRange? range = BlockBreakHardness?.TryGet(domain);
            if (range == null)
            {
                api.Logger.Notification("[prosequor] Block-break hardness ({0}): none.", domain);
                return;
            }

            api.Logger.Notification(
                "[prosequor] Block-break hardness ({0}): {1} blocks, Resistance {2:0.###}–{3:0.###}.",
                domain,
                range.Value.BlockCount,
                range.Value.Min,
                range.Value.Max);
        }

        ProgressCommands.Register(api);
        ClayFormCraftAttribution.RegisterServer(api);
        CraftMutateOutputStation.RegisterServer(api);
        FatherXp = new FatherXp(api, Registry);
        FatherXp.Load();
        ProgressPark = new ProgressPark();
        TryPreloadProgressPark();
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.GameWorldSave += OnFatherXpWorldSave;
        api.Event.PlayerJoin += OnPlayerJoin;
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        ActivityWatch = new ActivityWatchService(api, ActivityWrappers, XpRules);
        // Host (and anyone already spawned) inited before this listener existed.
        foreach (IServerPlayer player in api.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (TryGetLiveProgress(player) is EntityBehaviorProgress progress)
            {
                AdmitInitialized(player, progress);
            }
        }

        activityWatchListenerId = api.Event.RegisterGameTickListener(
            OnActivityWatchTick,
            PlayerWorkBuckets.TickMs);
        progressFlushListenerId = api.Event.RegisterGameTickListener(
            OnProgressFlushTick,
            150);
        animalAlertListenerId = api.Event.RegisterGameTickListener(
            _ => AnimalAlertService.Tick(AnimalAlertService.TickMs / 1000f),
            AnimalAlertService.TickMs);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        PlayerInteractionStation.ClientAfterBasicSlotsResized =
            CarryInventoryDialogPatch.RequestRecomposeIfOpen;
        Network.StartClient(api);
        catEyes = new CatEyesController(api);
        animalAlertOverlay = new AnimalAlertOverlayRenderer(api);
        api.ChatCommands
            .Create("alertviz")
            .WithDescription("Toggle Prosequor animal alert/threat overlay bars")
            .HandleWith(_ =>
            {
                if (animalAlertOverlay == null)
                {
                    return TextCommandResult.Error("Alert overlay not ready.");
                }

                animalAlertOverlay.Enabled = !animalAlertOverlay.Enabled;
                return TextCommandResult.Success(
                    animalAlertOverlay.Enabled
                        ? "Animal alert overlay on (thick=alert, thin=threat; cyan/amber/red)."
                        : "Animal alert overlay off.");
            });
        levelUpHud = new LevelUpHudController(api);
        levelUpHudElement = new HudElementLevelUp(api, levelUpHud);
        levelUpHudHandler = packet => levelUpHud?.Enqueue(packet);
        Network.LevelUpHudReceived += levelUpHudHandler;
        skillsTab = new CharacterSkillsTab(api, Network);
        skillWaitingHud = new SkillWaitingHudController(
            api,
            Network,
            skillId => skillsTab?.TrySelectTab(skillId) == true,
            () => skillWaitingHudElement?.IsOpened() == true);
        skillWaitingHudElement = new HudElementSkillWaiting(api, skillWaitingHud);
        statsPanel = new CharacterStatsPanel(api);

        ItemTooltipStatsBand.Register(ArmorTooltipStatsBand.TryProvide);
        ItemTooltipStatsBand.Register(ClothingTooltipStatsBand.TryProvide);
        ItemTooltipStatsBand.Register(WeaponToolTooltipStatsBand.TryProvide);

        // Tick observe must not wait on BlockTexturesLoaded — that event is easy to miss
        // timing-wise and is unrelated to unlock-point mirroring.
        skillWaitingHud.Start();

        skillWaitingDumpHandler = () =>
        {
            string dump = skillWaitingHud?.FormatStatusDump() ?? "skill-waiting controller missing";
            api.Logger.Notification("[prosequor] skillhint {0}", dump);
            api.ShowChatMessage("[prosequor] skillhint " + dump);
        };
        Network.SkillWaitingDumpRequested += skillWaitingDumpHandler;

        contentMismatchHandler = OnContentMismatch;
        Network.ContentMismatchReceived += contentMismatchHandler;

        api.ChatCommands
            .GetOrCreate("prosequor")
            .WithDescription("Prosequor client tools")
            .BeginSubCommand("fingerprint")
                .WithDescription("Show the local compiled-content fingerprint")
                .HandleWith(_ =>
                {
                    if (Fingerprint == null)
                    {
                        return TextCommandResult.Error("Content fingerprint is not ready.");
                    }

                    return TextCommandResult.Success(
                        $"v{Fingerprint.Version} {Fingerprint.Hash}");
                })
            .EndSubCommand();

        // Character dialog exists after BlockTexturesLoaded / gui load.
        // Stats compose is a Harmony postfix on Essentials ComposeStatsGui.
        api.Event.BlockTexturesLoaded += () =>
        {
            skillsTab?.Start();
            skillWaitingHud?.AttachCharacterDialog(
                api.Gui.LoadedGuis.Find(g => g is GuiDialogCharacterBase) as GuiDialogCharacterBase);
            statsPanel?.Start();
        };
    }

    public override void Dispose()
    {
        if (sapi != null)
        {
            sapi.Event.SaveGameLoaded -= OnSaveGameLoaded;
            sapi.Event.GameWorldSave -= OnFatherXpWorldSave;
            FatherXp?.Save();
            FatherXp = null;
            ProgressPark?.Clear();
            ProgressPark = null;
            sapi.Event.PlayerJoin -= OnPlayerJoin;
            sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;
            if (activityWatchListenerId != 0)
            {
                sapi.Event.UnregisterGameTickListener(activityWatchListenerId);
                activityWatchListenerId = 0;
            }

            if (progressFlushListenerId != 0)
            {
                sapi.Event.UnregisterGameTickListener(progressFlushListenerId);
                progressFlushListenerId = 0;
            }

            if (animalAlertListenerId != 0)
            {
                sapi.Event.UnregisterGameTickListener(animalAlertListenerId);
                animalAlertListenerId = 0;
            }

            Effort.UnregisterPoll(Effort.PollIdMount);
            Effort.UnregisterPoll(Effort.PollIdFishing);
            Effort.UnregisterPoll(Effort.PollIdFoot);
            Effort.UnregisterPoll(Effort.PollIdTemporalDrain);
        }

        blockBreakXpAdapter?.Dispose();
        blockBreakXpAdapter = null;
        fishingCatchXpAdapter?.Dispose();
        fishingCatchXpAdapter = null;
        craftXpAdapter?.Dispose();
        craftXpAdapter = null;
        CraftXp = null;
        clayFormXpAdapter?.Dispose();
        clayFormXpAdapter = null;
        ClayFormXp = null;
        ActivityWatch = null;
        XpDispatcher = null;
        Pipeline = null;

        catEyes?.Dispose();
        catEyes = null;
        skillsTab?.Dispose();
        skillsTab = null;
        statsPanel?.Dispose();
        statsPanel = null;
        CharacterTraitsTabPatches.Dispose();
        if (levelUpHudHandler != null)
        {
            Network.LevelUpHudReceived -= levelUpHudHandler;
            levelUpHudHandler = null;
        }

        if (skillWaitingDumpHandler != null)
        {
            Network.SkillWaitingDumpRequested -= skillWaitingDumpHandler;
            skillWaitingDumpHandler = null;
        }

        if (contentMismatchHandler != null)
        {
            Network.ContentMismatchReceived -= contentMismatchHandler;
            contentMismatchHandler = null;
        }

        Network.Stop();

        if (levelUpHudElement != null)
        {
            levelUpHudElement.TryClose();
            levelUpHudElement.Dispose();
            levelUpHudElement = null;
        }

        levelUpHud = null;
        if (skillWaitingHudElement != null)
        {
            skillWaitingHudElement.TryClose();
            skillWaitingHudElement.Dispose();
            skillWaitingHudElement = null;
        }

        skillWaitingHud = null;
        animalAlertOverlay?.Dispose();
        animalAlertOverlay = null;

        ItemTooltipStatsBand.ClearProviders();
        ItemstackInfoTooltipPatches.DisposeIcons();

        PlayerInteractionStation.ClientAfterBasicSlotsResized = null;
        capi = null;
        sapi = null;

        ReleasePatches();
    }

    void OnContentMismatch(ContentFingerprintMismatchPacket packet)
    {
        if (capi == null)
        {
            return;
        }

        capi.Logger.Warning(
            "[{0}] Content fingerprint mismatch: client v{1} {2} server v{3} {4}",
            ModId,
            packet.ClientSchema,
            packet.ClientHash,
            packet.ServerSchema,
            packet.ServerHash);

        string message = Lang.Get(
            "prosequor:content-mismatch",
            ContentFingerprint.Abbreviate(packet.ClientHash),
            ContentFingerprint.Abbreviate(packet.ServerHash));
        capi.ShowChatMessage(message);
        capi.TriggerIngameError(this, "prosequor-content", message);
    }

    void OnSaveGameLoaded()
    {
        FatherXp?.Load();
        TryPreloadProgressPark();
    }

    void TryPreloadProgressPark()
    {
        if (sapi == null || ProgressPark == null)
        {
            return;
        }

        int loaded = ProgressPark.Preload(sapi, Registry, Pipeline?.RuleIndex);
        if (loaded > 0)
        {
            sapi.Logger.Notification("[{0}] Parked progress for {1} player(s).", ModId, loaded);
        }
    }

    void OnFatherXpWorldSave() => FatherXp?.Save();

    void OnPlayerJoin(IServerPlayer byPlayer)
    {
        ProgressPark?.Evict(byPlayer.PlayerUID);
        // Reconnect can reuse an entity that already ran AfterInitialized (and was forgotten on leave).
        // Enroll before mailbox flush so Collect pays a loaded player, not a half-ready one.
        if (TryGetLiveProgress(byPlayer) is EntityBehaviorProgress progress)
        {
            AdmitInitialized(byPlayer, progress);
        }

        FatherXp?.Collect(byPlayer);
    }

    void OnPlayerNowPlaying(IServerPlayer byPlayer)
    {
        if (TryGetLiveProgress(byPlayer) is EntityBehaviorProgress progress)
        {
            AdmitInitialized(byPlayer, progress);
        }
    }

    void OnPlayerDisconnect(IServerPlayer byPlayer)
    {
        ActivityWatch?.ForgetPlayer(byPlayer.PlayerUID);
        EntityBehaviorProgress? progress = TryGetLiveProgress(byPlayer);
        progress?.FlushSave();
        ProgressPark?.ParkLive(byPlayer.PlayerUID, progress, Pipeline?.RuleIndex, Registry);
    }

    /// <summary>
    /// Enroll a player whose progress behavior has finished init. Idempotent.
    /// </summary>
    public void AdmitInitialized(IServerPlayer player, EntityBehaviorProgress progress)
    {
        progress.EnsureLoaded(player, Registry);
        ActivityWatch?.RememberPlayer(player.PlayerUID);
        TryApplyClassProfile(player);
    }

    void TryApplyClassProfile(IServerPlayer player)
    {
        if (sapi == null)
        {
            return;
        }

        CharacterSystem? characters = sapi.ModLoader.GetModSystem<CharacterSystem>();
        if (characters == null)
        {
            return;
        }

        TraitAttributeConverter.TryApplyOnSelection(player, characters, TraitAttributes, Registry);
    }

    void OnActivityWatchTick(float dt)
    {
        if (ActivityWatch == null)
        {
            return;
        }

        IReadOnlyList<IServerPlayer> slice = ActivityWatch.ResolveCurrent();
        int generation = ActivityWatch.CurrentGeneration;
        ActivityWatch.Tick(slice);
        RidingPlayerStats.Tick(slice);
        ActivityWatch.TickMaintenance(slice, generation);
        ActivityWatch.Advance();
    }

    void OnProgressFlushTick(float dt)
    {
        if (sapi == null)
        {
            return;
        }

        foreach (IPlayer player in sapi.World.AllOnlinePlayers)
        {
            if (TryGetLiveProgress(player) is EntityBehaviorProgress progress)
            {
                progress.FlushPendingCoalesced();
            }
        }
    }

    /// <summary>Register a thin activity wrapper (other mods). Last register for the same id wins.
    /// Deprecated for rate XP — prefer <see cref="RegisterEffortPoll"/> or <see cref="EmitEffort"/>.
    /// </summary>
    public void RegisterActivityWrapper(IActivityWrapper wrapper) =>
        ActivityWrappers.Register(wrapper);

    /// <summary>
    /// Register a watcher poll for continuous <c>prosequor:effort</c> when there is no natural
    /// emit site. Return null when inactive; otherwise tokens + roles. Same id last-wins.
    /// Prefer <see cref="EmitEffort"/> when the game already pulses.
    /// </summary>
    public void RegisterEffortPoll(string id, EffortPoll poll) =>
        Effort.RegisterPoll(id, poll);

    /// <summary>Remove a previously registered effort poll.</summary>
    public void UnregisterEffortPoll(string id) =>
        Effort.UnregisterPoll(id);

    /// <summary>
    /// Push an effort stamp for rate XP (<c>prosequor:effort</c>). Prefer <see cref="EffortToken"/>
    /// for standard tags; supply target/mount/ground the emitter already knows.
    /// </summary>
    public void EmitEffort(
        IPlayer player,
        EffortToken token,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? channel = null) =>
        Effort.Emit(player, token, target, mount, ground, channel);

    /// <inheritdoc cref="EmitEffort(IPlayer, EffortToken, string?, string?, string?, string?)"/>
    public void EmitEffort(
        IPlayer player,
        IReadOnlyList<EffortToken> tokens,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? channel = null) =>
        Effort.Emit(player, tokens, target, mount, ground, channel);

    /// <inheritdoc cref="EmitEffort(IPlayer, EffortToken, string?, string?, string?, string?)"/>
    public void EmitEffort(
        IPlayer player,
        IReadOnlyList<string> tokens,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? channel = null) =>
        Effort.Emit(player, tokens, target, mount, ground, channel);

    /// <summary>
    /// Award discrete amount XP (<c>prosequor:deed</c>) for a payee. Resolves at the call and
    /// pays through FatherXp (online or mailbox). Prefer <see cref="DeedToken"/> for standard tags.
    /// Quantity and ingredients are summed from the unit lists. Measures are read from
    /// <paramref name="target"/> / <paramref name="subject"/>.
    /// </summary>
    public void EmitDeed(
        string playerUid,
        DeedToken token,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        BlockPos? position = null,
        IReadOnlyList<Deed.QuantityUnit>? outputs = null,
        IReadOnlyList<Deed.QuantityUnit>? inputs = null,
        ItemStack? subject = null) =>
        Deed.Emit(
            sapi!,
            playerUid,
            token,
            caller,
            target,
            mount,
            ground,
            lastCraft,
            position,
            outputs,
            inputs,
            subject: subject);

    /// <inheritdoc cref="EmitDeed(string, DeedToken, string?, string?, string?, string?, string?, BlockPos?, IReadOnlyList{Deed.QuantityUnit}?, IReadOnlyList{Deed.QuantityUnit}?, ItemStack?)"/>
    public void EmitDeed(
        string playerUid,
        IReadOnlyList<string> tokens,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        BlockPos? position = null,
        IReadOnlyList<Deed.QuantityUnit>? outputs = null,
        IReadOnlyList<Deed.QuantityUnit>? inputs = null,
        ItemStack? subject = null) =>
        Deed.Emit(
            sapi!,
            playerUid,
            tokens,
            caller,
            target,
            mount,
            ground,
            lastCraft,
            position,
            outputs,
            inputs,
            subject: subject);

    /// <inheritdoc cref="EmitDeed(string, DeedToken, string?, string?, string?, string?, string?, BlockPos?, IReadOnlyList{Deed.QuantityUnit}?, IReadOnlyList{Deed.QuantityUnit}?, ItemStack?)"/>
    public void EmitDeed(
        IPlayer player,
        IReadOnlyList<string> tokens,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        BlockPos? position = null,
        IReadOnlyList<Deed.QuantityUnit>? outputs = null,
        IReadOnlyList<Deed.QuantityUnit>? inputs = null,
        ItemStack? subject = null) =>
        Deed.Emit(
            sapi!,
            player,
            tokens,
            caller,
            target,
            mount,
            ground,
            lastCraft,
            position,
            outputs,
            inputs,
            subject: subject);

    internal void PublishLevelUp(LevelUpEvent evt)
    {
        ProgressEvents.Publish(evt);
        if (evt.SuppressDefaultNotification)
        {
            return;
        }

        string message;
        if (evt.Track == ProgressTrack.Player)
        {
            message = Lang.GetL(
                evt.Player.LanguageCode,
                "prosequor:levelup-player",
                evt.NewLevel);
        }
        else
        {
            string skillName = evt.SkillId != null && Registry.TryGet(evt.SkillId, out SkillDef def)
                ? SkillRegistry.DisplayName(def, evt.Player.LanguageCode)
                : evt.SkillId ?? "";
            message = Lang.GetL(
                evt.Player.LanguageCode,
                "prosequor:levelup-skill",
                skillName,
                evt.NewLevel);
        }

        string data = JsonConvert.SerializeObject(new
        {
            type = "prosequor:level-up",
            version = 1,
            track = evt.Track == ProgressTrack.Player ? "player" : "skill",
            skill = evt.SkillId,
            previousLevel = evt.PreviousLevel,
            newLevel = evt.NewLevel
        });

        evt.Player.SendMessage(
            GlobalConstants.InfoLogChatGroup,
            message,
            EnumChatType.Notification,
            data);
    }

    public static IPlayerProgress? GetProgress(IPlayer player) =>
        TryGetLiveProgress(player);

    /// <summary>
    /// Entity.GetBehavior NREs when SidedProperties is null (Properties / Server unset).
    /// Online players can be in that state during spawn or skipped character selection.
    /// </summary>
    public static EntityBehaviorProgress? TryGetLiveProgress(IPlayer? player)
    {
        Entity? entity = player?.Entity;
        if (entity?.SidedProperties?.Behaviors == null)
        {
            return null;
        }

        return entity.GetBehavior<EntityBehaviorProgress>();
    }

    /// <summary>
    /// Online: the entity behavior. Offline: the parked snapshot, if this uptime loaded it.
    /// </summary>
    public static IPlayerProgress? GetProgress(ICoreAPI? api, string? playerUid)
    {
        if (api == null || string.IsNullOrWhiteSpace(playerUid))
        {
            return null;
        }

        ProsequorModSystem? mod = For(api);
        if (mod == null)
        {
            return null;
        }

        if (mod.sapi?.World.PlayerByUid(playerUid) is IPlayer player)
        {
            IPlayerProgress? live = TryGetLiveProgress(player);
            if (live != null)
            {
                return live;
            }
        }

        return mod.ProgressPark?.TryGet(playerUid);
    }

    /// <summary>Called from Harmony OnBlockBroken postfix (server only).</summary>
    internal void NotifyBlockBrokenXp(IPlayer byPlayer, Block broken, BlockPos pos) =>
        blockBreakXpAdapter?.NotifyBlockBroken(byPlayer, broken, pos);

    /// <summary>Called from Harmony OnBlockExploded postfix (server only).</summary>
    internal void NotifyBlockExplodedXp(
        string? playerUid,
        Block broken,
        BlockPos pos,
        string? bombCaller) =>
        blockBreakXpAdapter?.NotifyBlockExploded(playerUid, broken, pos, bombCaller);

    /// <summary>Called from fishing catch-scope EntityPlayer.TryGiveItemStack prefix (server only).</summary>
    internal void NotifyFishCatchDrops(IPlayer byPlayer, ItemStack caught) =>
        FishingCatchDrops.ApplyPrefix(sapi!.World, byPlayer, caught);

    /// <summary>Called from fishing catch-scope EntityPlayer.TryGiveItemStack postfix (server only).</summary>
    internal void NotifyFishCatchXp(IPlayer byPlayer, ItemStack caught) =>
        fishingCatchXpAdapter?.NotifyCatch(byPlayer, caught);
}
