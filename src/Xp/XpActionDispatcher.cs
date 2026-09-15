using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Xp;

/// <summary>
/// Legacy discrete-action entry. Prefer <see cref="Deed.Emit"/>; this forwards to it.
/// </summary>
public class XpActionDispatcher
{
    readonly ICoreServerAPI sapi;

    public XpActionDispatcher(ICoreServerAPI sapi, IXpRuleRegistry rules)
    {
        this.sapi = sapi;
        _ = rules;
    }

    public void Emit(XpAction action) => Deed.Emit(sapi, action);
}
