using Content.Server.Power.Components;

namespace Content.Server._NF.Atmos.Components;

[RegisterComponent]
public sealed partial class GaslockPowerTransferComponent : Component
{
    [DataField("maxPowerTransfer")]
    public float MaxPowerTransfer = 10_000f;

    [ViewVariables]
    public EntityUid? SourceDock;

    [ViewVariables]
    public ApcPowerProviderComponent? SourceProvider;
}
