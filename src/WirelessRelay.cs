using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Game;
using UI;
using UnityEngine;
using VoxelEngine;

namespace RadioLogic
{
    public sealed class RelaySettings
    {
        public string Endpoint {get;set;}
        public string Peer {get;set;}
        public int AntennaBrick {get;set;}
        public int AntennaChild {get;set;}=-1;
        [CommandCustomizableField("Name",null)] public string Label {get;set;}
        [CommandInfoProperty("Peer: {0}", "{0}", true, true)] public string PeerName {get;set;}
        [CommandCustomizableField("Pair with",null)] public List<string> PairOptions {get;set;}
        // Native dynamic dropdowns consume the immediately following integer property.
        public int SelectedPair {get;set;}
        [CommandCustomizableAction("Pair")] public Action PairAction {get;set;}
        [CommandCustomizableAction("Unpair")] public Action UnpairAction {get;set;}
        [CommandInfoProperty("Antenna: {0}", "{0}", true, true)] public string AntennaName {get;set;}
        [CommandInfoProperty("{0}", "{0}", true, true)] public string PowerHelp=>"Both antennas need an ON signal. Enable each antenna with a switch or logic input.";
        [CommandCustomizableAction("Unbind antenna")] public Action ClearAntennaAction {get;set;}
        [CommandCustomizableFieldBool("Input","Pulse","Signal")] public bool Pulse {get;set;}
        [CommandInfoProperty("Link: {0}", "{0}", true, true)] public string Status {get;set;}
    }
    public abstract class WirelessRelay:Command
    {
        internal RelaySettings Settings=new RelaySettings();
        private readonly List<WirelessRelay> pairChoices=new List<WirelessRelay>();
        private string savedGrid;
        internal bool Connected;
        internal string Reason="Not paired";
        public string Endpoint=>Settings.Endpoint;
        internal string FriendlyName=>parentEntity.EntityName+" / "+(string.IsNullOrWhiteSpace(CommandLabel)?OverTextIndirect:CommandLabel);
        public override string OverText=>"Configure Wireless Relay";
        public override string OverTextIndirect=>this is WirelessTransmitter?"Wireless Transmitter":"Wireless Receiver";
        public override CommandActionIcon ActionIcon=>CommandActionIcon.configure;
        public override bool CanActivateManually(PlayerCommandManager player)=>true;
        protected override void OnInitialize(BrickIds ids,bool onLoad,params object[] args)
        {
            vertexDataPointer=MaterialDynamicData.RegisterVector();
            pixelDataPointer=MaterialDynamicData.RegisterFloat(0);
            Settings.Endpoint=Guid.NewGuid().ToString("N");
            Settings.AntennaChild=-1;
            if(!onLoad)
            {
                int number=1;
                foreach(var relay in RadioNetwork.Relays)if(relay.parentEntity==parentEntity && relay.GetType()==GetType())number++;
                CommandLabel=(this is WirelessTransmitter?"Transmitter ":"Receiver ")+number;
            }
            RadioNetwork.Relays.Add(this);
        }
        internal void OpenSettings()
        {
            if(!Plugin.MenuReady){Plugin.Log.LogWarning("RF configuration unavailable: native menu contracts changed");return;}
            OpenSettingsNative();
        }
        private void OpenSettingsNative()
        {
            Settings.Label=CommandLabel;
            var peer=RadioNetwork.Find(Settings.Peer);
            Settings.PeerName=peer==null?(string.IsNullOrEmpty(Settings.Peer)?"Not paired":"Paired relay unavailable"):peer.FriendlyName;
            Settings.AntennaName=RadioNetwork.AntennaLabel(this);
            int selected=Settings.SelectedPair;
            string selectedId=selected>0 && selected<pairChoices.Count?pairChoices[selected]?.Endpoint:null;
            pairChoices.Clear();pairChoices.Add(null);
            Settings.PairOptions=new List<string>{"Choose an available relay"};Settings.SelectedPair=0;
            foreach(var relay in RadioNetwork.Relays)
            {
                if(!RadioNetwork.EligiblePair(this,relay))continue;
                pairChoices.Add(relay);Settings.PairOptions.Add(relay.FriendlyName+" ("+relay.brickData.Position.ToString("F1")+")");
                if(relay.Endpoint==selectedId)Settings.SelectedPair=pairChoices.Count-1;
            }
            Settings.PairAction=PairChosen;
            Settings.UnpairAction=()=>{RadioNetwork.Unpair(this);OpenSettings();};
            Settings.ClearAntennaAction=()=>{RadioNetwork.ClearAntenna(this);OpenSettings();};
            Settings.Status=RadioNetwork.Status(this);
            int mask=0;
            if(this is WirelessReceiver)CommandCommon.BuildFieldMask<RelaySettings>(ref mask,nameof(RelaySettings.Pulse));
            UI_CommandGeneric.OpenConfig(new UI_CommandGeneric.WindowsParams {command=this,title=OverTextIndirect,commandData=Settings,brickCoordForLabel=brickCoord,fieldMask=mask});
        }
        private void PairChosen()
        {
            int i=Settings.SelectedPair;
            if(i>0 && i<pairChoices.Count && RadioNetwork.EligiblePair(this,pairChoices[i]))
                RadioNetwork.PairEndpoints(this,pairChoices[i]);
            else Reason="Choose an available remote relay";
            OpenSettings();
        }
        protected override void OnGenericParamsUpdated(object value,bool onClose)
        {
            var incoming=(RelaySettings)value;
            incoming.Endpoint=Endpoint;
            Settings=incoming;
            CommandLabel=string.IsNullOrWhiteSpace(Settings.Label)?OverTextIndirect:Settings.Label.Trim();
            Connected=false;
            if(this is WirelessReceiver rx)rx.Deliver(false);
            if(this is WirelessTransmitter tx)tx.PendingPulses=0;
            if(!onClose)OpenSettings();
        }
        public override void OnStateUpdated(bool onLoad)
        {
            base.OnStateUpdated(onLoad);
            if(pixelDataPointer.id!=0)MaterialDynamicData.FloatDynDatas[pixelDataPointer.id]=State>.5f?1:0.1f;
        }
        public override object GetGizmo(UnityEngine.Rendering.CommandBuffer buffer)
        {
            if(!Plugin.MenuReady)return null;
            var peer=RadioNetwork.Find(Settings.Peer);
            Settings.PeerName=peer==null?"Not available":peer.FriendlyName;
            Settings.AntennaName=RadioNetwork.AntennaLabel(this);
            Settings.Status=RadioNetwork.Status(this);
            return Settings;
        }
        public override void OnDestroy(BrickUnspawnMode mode)
        {
            Connected=false;
            RadioNetwork.Relays.Remove(this);
            base.OnDestroy(mode);
        }
        public override byte[] GetCustomData(bool isBlueprintExport)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8))
            {
                writer.Write(1);writer.Write(isBlueprintExport?"":Endpoint??"");
                writer.Write(isBlueprintExport?"":parentEntity.SGUID??"");
                writer.Write(isBlueprintExport?"":Settings.Peer??"");
                writer.Write(Settings.AntennaChild);writer.Write(Settings.AntennaBrick);writer.Write(Settings.Pulse);
                writer.Flush();return stream.ToArray();
            }
        }
        public override void OnSaveLoaded(byte[] bytes,bool onInit)
        {
            State=0;Connected=false;
            if(bytes==null || bytes.Length==0)return;
            try
            {
                if(bytes.Length>2048)throw new InvalidDataException("Oversized RF settings");
                using(var reader=new BinaryReader(new MemoryStream(bytes),Encoding.UTF8))
                {
                    if(reader.ReadInt32()!=1)throw new InvalidDataException("Unknown RF save schema");
                    string id=reader.ReadString();savedGrid=reader.ReadString();string peer=reader.ReadString();
                    Settings.AntennaChild=reader.ReadInt32();Settings.AntennaBrick=reader.ReadInt32();Settings.Pulse=reader.ReadBoolean();
                    bool unique=true;
                    foreach(var other in RadioNetwork.Relays)if(other!=this && other.Endpoint==id)unique=false;
                    if(Guid.TryParseExact(id,"N",out _) && savedGrid==parentEntity.SGUID && unique)
                    {Settings.Endpoint=id;Settings.Peer=peer;}
                    else{Settings.Endpoint=Guid.NewGuid().ToString("N");Settings.Peer="";Reason="New/copy identity; pair again";}
                }
            }
            catch(Exception error){Settings.Peer="";Reason="Invalid saved settings";Plugin.Log.LogWarning(error.Message);}
        }
    }
    public sealed class WirelessTransmitter:WirelessRelay
    {
        internal int PendingPulses;
        internal bool Input;
        public override bool CanBeLinkSource=>true;
        public override bool CanLinkToCommand(ICommand target)=>target is AntennaCommandData;
        public override void Activate(bool manualActivation,PlayerCommandManager player,float state)
        {
            if(manualActivation){if(player!=null && player.isLocalPlayer)OpenSettings();return;}
            if(IsRemoved || float.IsNaN(state) || state<0)return;
            bool next=state>.5f;
            if(Settings.Pulse && next && !Input && RadioNetwork.Path(this,out _,out _))PendingPulses=Math.Min(32,PendingPulses+1);
            Input=next;State=next?1:0;OnStateUpdated(false);
        }
        public override void OnPredecessorRemoved(ICommand source){Input=false;State=0;PendingPulses=0;}
    }
    public sealed class WirelessReceiver:WirelessRelay
    {
        internal float PulseUntil;
        internal int LowFrame;
        internal int DeliveredPulses;
        private bool synchronizeOutput=true;
        // Visible to the native link tool as an antenna binding destination;
        // ordinary local signal sources are still rejected.
        public override bool CanBeLinkTarget=>true;
        public override bool CanBeLinkTargetFromCommand(ICommand source)=>source is AntennaLinkSource;
        public override void Activate(bool manualActivation,PlayerCommandManager player,float state)
        {if(manualActivation && player!=null && player.isLocalPlayer)OpenSettings();}
        internal void Deliver(bool value)
        {
            float next=value?1:0;
            if(State==next && !synchronizeOutput)return;
            synchronizeOutput=false;
            State=next;OnStateUpdated(false);base.Propagate();
        }
        public override void OnSaveLoaded(byte[] bytes,bool onInit)
        {base.OnSaveLoaded(bytes,onInit);synchronizeOutput=true;}
        public override void OnTargetAdded(ICommand target,bool onLoad)
        {if(!onLoad)target.Activate(false,null,State);}
    }
}
