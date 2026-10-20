using Content.Server._NF.Atmos.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;

namespace Content.Server._NF.Atmos.Systems;

public sealed class GaslockPowerTransferSystem : EntitySystem
{
    [Dependency] private readonly ExtensionCableSystem _extensionCable = default!;
    [Dependency] private readonly PowerNetSystem _powerNet = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(PowerNetSystem));

        SubscribeLocalEvent<GaslockPowerTransferComponent, DockEvent>(OnDock);
        SubscribeLocalEvent<GaslockPowerTransferComponent, UndockEvent>(OnUndock);
    }

    private void OnDock(Entity<GaslockPowerTransferComponent> ent, ref DockEvent args)
    {
        if (!TryComp<DockingComponent>(ent, out var docking) || docking.DockedWith is not { } sourceDock)
            return;

        if (ent.Comp.SourceDock != sourceDock)
        {
            Disconnect(ent);
            ent.Comp.SourceDock = sourceDock;

            if (TryComp<ExtensionCableReceiverComponent>(ent, out var extensionReceiver))
                _extensionCable.Disconnect((ent.Owner, extensionReceiver));
        }

        UpdateTransfer(ent);
    }

    private void OnUndock(Entity<GaslockPowerTransferComponent> ent, ref UndockEvent args)
    {
        if (ent.Comp.SourceDock == null)
            return;

        Disconnect(ent);
        ent.Comp.SourceDock = null;

        if (TryComp<ExtensionCableReceiverComponent>(ent, out var extensionReceiver))
            _extensionCable.Connect((ent.Owner, extensionReceiver));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GaslockPowerTransferComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.SourceDock == null)
                continue;

            UpdateTransfer((uid, component));
        }
    }

    private void UpdateTransfer(Entity<GaslockPowerTransferComponent> ent)
    {
        if (!TryComp<ApcPowerReceiverComponent>(ent, out var receiver) ||
            !TryComp<PowerConsumerComponent>(ent, out var input) ||
            !TryComp<PowerSupplierComponent>(ent, out var output))
            return;

        ApcPowerProviderComponent? sourceProvider = null;
        if (ent.Comp.SourceDock is { } sourceDock &&
            TryComp<ApcPowerReceiverComponent>(sourceDock, out var sourceReceiver))
        {
            sourceProvider = sourceReceiver.Provider;
        }

        if (!ReferenceEquals(sourceProvider, ent.Comp.SourceProvider))
        {
            if (ent.Comp.SourceProvider != null)
                ent.Comp.SourceProvider.RemoveReceiver(receiver);

            if (receiver.Provider != null && receiver.Provider != ent.Comp.SourceProvider)
                receiver.Provider.RemoveReceiver(receiver);

            receiver.Provider = sourceProvider;
            sourceProvider?.AddReceiver(receiver);
            ent.Comp.SourceProvider = sourceProvider;
        }

        var sourceNet = sourceProvider?.Net;
        if (input.Net != sourceNet)
            input.Net = sourceNet;

        if (sourceNet == null || output.Net == null)
        {
            input.DrawRate = 0f;
            output.MaxSupply = 0f;
            output.Enabled = false;
            return;
        }

        output.Enabled = true;
        var outputStats = _powerNet.GetNetworkStatistics(output.Net.NetworkNode);
        var supplyWithoutTransfer = Math.Max(0f, outputStats.SupplyCurrent - output.CurrentSupply);
        var unmetDemand = Math.Max(0f, outputStats.Consumption - supplyWithoutTransfer);

        input.DrawRate = Math.Min(ent.Comp.MaxPowerTransfer, unmetDemand);
        output.MaxSupply = Math.Min(ent.Comp.MaxPowerTransfer, input.ReceivedPower);
    }

    private void Disconnect(Entity<GaslockPowerTransferComponent> ent)
    {
        if (TryComp<ApcPowerReceiverComponent>(ent, out var receiver))
        {
            ent.Comp.SourceProvider?.RemoveReceiver(receiver);
            if (receiver.Provider != null && receiver.Provider != ent.Comp.SourceProvider)
                receiver.Provider.RemoveReceiver(receiver);

            receiver.Provider = null;
        }

        if (TryComp<PowerConsumerComponent>(ent, out var input))
        {
            input.DrawRate = 0f;
            input.Net = null;
        }

        if (TryComp<PowerSupplierComponent>(ent, out var output))
        {
            output.Enabled = false;
            output.MaxSupply = 0f;
        }

        ent.Comp.SourceProvider = null;
    }
}
