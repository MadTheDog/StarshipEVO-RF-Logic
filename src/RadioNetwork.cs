using System;
using System.Collections.Generic;
using VoxelEngine;
using UnityEngine;

namespace RadioLogic
{
    internal static class RadioNetwork
    {
        internal static readonly List<WirelessRelay> Relays=new List<WirelessRelay>();
        private static readonly Dictionary<string,WirelessRelay> Endpoints=new Dictionary<string,WirelessRelay>();
        private static bool Available(WirelessRelay relay)=>relay!=null && !relay.IsRemoved && relay.parentEntity!=null && relay.parentEntity.IsInitialized && !relay.parentEntity.WillBeUnspawned && !relay.parentEntity.isDestroyed;
        internal static WirelessRelay Find(string endpoint)
        {
            if(string.IsNullOrEmpty(endpoint))return null;
            foreach(var relay in Relays)if(relay.Endpoint==endpoint && Available(relay))return relay;
            return null;
        }
        internal static bool EligiblePair(WirelessRelay first,WirelessRelay second)
        {
            if(PlayerState.sessionMode!=PlayerState.SessionMode.solo || !Available(first) || !Available(second))return false;
            var a=first.parentEntity;var b=second.parentEntity;
            bool occupied=(!string.IsNullOrEmpty(first.Settings.Peer) && first.Settings.Peer!=second.Endpoint)
                || (!string.IsNullOrEmpty(second.Settings.Peer) && second.Settings.Peer!=first.Endpoint);
            bool friendly=a.Identity.IsPlayerOwned && b.Identity.IsPlayerOwned && !a.EntityMetaData.isWorldWreck && !b.EntityMetaData.isWorldWreck
                && a.SkyTransform.CurrentSystem.Equals(b.SkyTransform.CurrentSystem);
            return RadioPolicy.CanPair(first is WirelessTransmitter,second is WirelessTransmitter,a!=b,friendly,occupied);
        }
        internal static void PairEndpoints(WirelessRelay first,WirelessRelay second)
        {
            if(!EligiblePair(first,second))throw new InvalidOperationException("Pairing refused: choose free opposite relay on a separate friendly grid.");
            first.Settings.Peer=second.Endpoint;second.Settings.Peer=first.Endpoint;
            Reset(first);Reset(second);
            first.Reason=second.Reason="Paired; link each relay to its local antenna";
        }
        internal static void Unpair(WirelessRelay relay)
        {
            var peer=Find(relay.Settings.Peer);
            if(peer!=null && peer.Settings.Peer==relay.Endpoint){peer.Settings.Peer="";Reset(peer);}
            relay.Settings.Peer="";Reset(relay);relay.Reason="Not paired";
        }
        private static void Reset(WirelessRelay relay)
        {
            relay.Connected=false;
            if(relay is WirelessTransmitter tx)tx.PendingPulses=0;
            if(relay is WirelessReceiver rx){rx.PulseUntil=0;rx.Deliver(false);}
            relay.parentEntity?.SetDirty();
        }
        internal static void ClearAntenna(WirelessRelay relay)
        {relay.Settings.AntennaBrick=0;relay.Settings.AntennaChild=-1;Reset(relay);relay.Reason="No antenna bound; use the link tool";}
        internal static bool CanBind(WirelessRelay relay,BrickCoord antenna)
            =>PlayerState.sessionMode==PlayerState.SessionMode.solo && Available(relay) && antenna.IsValid
              && antenna.Parent==relay.parentEntity && ReferenceEquals(antenna.brickData.Brick,TabSystem.ANTENNA);
        internal static void Bind(WirelessRelay relay,BrickCoord antenna,bool toggle)
        {
            if(!CanBind(relay,antenna))throw new InvalidOperationException("Bind an antenna on the relay's own grid.");
            if(toggle && relay.Settings.AntennaBrick==antenna.brickData.instanceId && relay.Settings.AntennaChild==antenna.entityID.childrenID)
            {ClearAntenna(relay);return;}
            relay.Settings.AntennaBrick=antenna.brickData.instanceId;relay.Settings.AntennaChild=antenna.entityID.childrenID;
            Reset(relay);relay.Reason="Antenna bound; its normal logic input controls radio power";
        }
        internal static string AntennaLabel(WirelessRelay relay)
        {
            if(relay.parentEntity==null || relay.Settings.AntennaBrick<=0)return "Not bound";
            var entity=relay.parentEntity.Tree.Get(relay.Settings.AntennaChild);
            int id=relay.Settings.AntennaBrick;
            if(entity==null || id>=entity.Bricks.brickDatas.Length || !ReferenceEquals(entity.Bricks.brickDatas[id].Brick,TabSystem.ANTENNA))return "Unavailable";
            var coord=new BrickCoord(entity.ID,entity.Bricks.brickDatas[id]);
            string label=coord.TagData.label;
            string name=string.IsNullOrEmpty(label)?"Antenna":label;
            bool active=coord.Command is AntennaCommandData antenna && antenna.State>.5f;
            return (active?"ON":"OFF")+" | "+RadioPolicy.Range(coord.brickData.gridLevel)+" m | "+name;
        }
        internal static string Status(WirelessRelay relay)
        {
            if(string.IsNullOrEmpty(relay.Settings.Peer))return "Not paired";
            var tx=relay as WirelessTransmitter ?? Find(relay.Settings.Peer) as WirelessTransmitter;
            if(tx==null)return "Transmitter unavailable";
            return Path(tx,out _,out string reason)?"Connected":reason;
        }
        internal static bool Path(WirelessTransmitter tx,out WirelessReceiver rx,out string reason)
        {
            rx=null;reason="Solo mode required";
            if(PlayerState.sessionMode!=PlayerState.SessionMode.solo)return false;
            reason="Endpoint unavailable";
            if(!Available(tx) || string.IsNullOrEmpty(tx.Settings.Peer) || !Endpoints.TryGetValue(tx.Settings.Peer,out var remote) || !(remote is WirelessReceiver receiver) || !Available(receiver))return false;
            rx=receiver;
            reason="Pairing must be reciprocal";
            if(rx.Settings.Peer!=tx.Endpoint)return false;
            reason="Separate friendly player grids required";
            var a=tx.parentEntity;var b=rx.parentEntity;
            if(a==b || !a.Identity.IsPlayerOwned || !b.Identity.IsPlayerOwned || a.EntityMetaData.isWorldWreck || b.EntityMetaData.isWorldWreck)return false;
            reason="Different star systems";
            if(!a.SkyTransform.CurrentSystem.Equals(b.SkyTransform.CurrentSystem))return false;
            if(!ReadAntenna(tx,out var antennaA,out string antennaReason))
            {reason="TX "+antennaReason;return false;}
            if(!ReadAntenna(rx,out var antennaB,out antennaReason))
            {reason="RX "+antennaReason;return false;}
            var pa=antennaA.brickCoord.entity.TransformData.LocalToSystem(antennaA.brickCoord.Position);
            var pb=antennaB.brickCoord.entity.TransformData.LocalToSystem(antennaB.brickCoord.Position);
            double distance=Vector3l.Distance(pa,pb);
            reason="Out of mutual RF range";
            if(!RadioPolicy.Reach(antennaA.brickCoord.brickData.gridLevel,antennaB.brickCoord.brickData.gridLevel,distance))return false;
            reason="Paired antennas in range";return true;
        }
        internal static bool Antenna(WirelessRelay relay,out AntennaCommandData antenna)
            =>ReadAntenna(relay,out antenna,out _);
        private static bool ReadAntenna(WirelessRelay relay,out AntennaCommandData antenna,out string reason)
        {
            antenna=default;reason="antenna not bound";
            if(relay.Settings.AntennaBrick<=0)return false;
            var entity=relay.parentEntity.Tree.Get(relay.Settings.AntennaChild);
            int id=relay.Settings.AntennaBrick;
            reason="antenna unavailable";
            if(entity==null || entity.WillBeUnspawned || id<=0 || id>=entity.Bricks.brickDatas.Length)return false;
            var data=entity.Bricks.brickDatas[id];
            if(!data.IsValid || !ReferenceEquals(data.Brick,TabSystem.ANTENNA) || data.HealthFactor<=0)return false;
            if(!(data.Brick.GetCommand(entity.Bricks.brickIds[id]) is AntennaCommandData found))return false;
            reason="antenna off";
            if(found.State<=.5f)return false;
            reason="antenna ready";
            antenna=found;return true;
        }
        internal static void Tick()
        {
            Endpoints.Clear();
            foreach(var relay in Relays)
            {
                relay.Connected=false;
                if(!Available(relay))continue;
                if(Endpoints.ContainsKey(relay.Endpoint)){Endpoints[relay.Endpoint]=null;continue;}
                Endpoints.Add(relay.Endpoint,relay);
            }
            foreach(var relay in Relays)
            {
                if(!(relay is WirelessTransmitter tx))continue;
                if(!Path(tx,out var rx,out string reason))
                {tx.Reason=reason;tx.PendingPulses=0;continue;}
                tx.Connected=rx.Connected=true;tx.Reason=rx.Reason=reason;
                if(!tx.Settings.Pulse)
                {
                    // Native predecessor state survives save/load and reconnect independently of transient RF output.
                    tx.Input=tx.predecessors.Command!=null && tx.predecessors.Command.State>.5f;
                    float inputState=tx.Input?1:0;
                    if(tx.State!=inputState){tx.State=inputState;tx.OnStateUpdated(false);}
                    tx.PendingPulses=0;rx.Deliver(tx.Input);continue;
                }
                if(rx.State>.5f)
                {if(Time.time>=rx.PulseUntil){rx.Deliver(false);rx.LowFrame=Time.frameCount;}continue;}
                if(tx.PendingPulses>0 && Time.frameCount>rx.LowFrame)
                {tx.PendingPulses--;rx.DeliveredPulses++;rx.PulseUntil=Time.time+.2f;rx.Deliver(true);}
            }
            foreach(var relay in Relays)
                if(relay is WirelessReceiver rx && !rx.Connected)
                {rx.Deliver(false);rx.PulseUntil=0;rx.Reason="Pair unavailable, inactive, unauthorized or out of range";}
        }
        internal static void FailClosed()
        {foreach(var relay in Relays){relay.Connected=false;if(relay is WirelessReceiver rx)rx.Deliver(false);if(relay is WirelessTransmitter tx)tx.PendingPulses=0;}}
        internal static void Pair(WirelessTransmitter tx,WirelessReceiver rx,int txAntenna,int rxAntenna)
        {
            PairEndpoints(tx,rx);
            Bind(tx,new BrickCoord(tx.parentEntity.ID,tx.parentEntity.Bricks.brickDatas[txAntenna]),false);
            Bind(rx,new BrickCoord(rx.parentEntity.ID,rx.parentEntity.Bricks.brickDatas[rxAntenna]),false);
        }
    }
}
