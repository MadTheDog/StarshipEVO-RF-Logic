using System;
using Game;
using HarmonyLib;
using VoxelEngine;

namespace RadioLogic
{
    // Link-tool-only adapter. Native antenna lookup, activation and control links
    // continue to use AntennaCommandData; RF bindings live in relay save data.
    internal sealed class AntennaLinkSource:ICommandLinkSource
    {
        private readonly BrickCoord coord;
        internal AntennaLinkSource(BrickCoord coord){this.coord=coord;}
        private AntennaCommandData Native=>(AntennaCommandData)coord.Command;
        public BrickCoord brickCoord=>coord;
        public float State {get=>Native.State;set{var a=Native;a.State=value;a.PushItself();}}
        public bool CanBeLinkTarget=>true;
        public bool AllowMultipleInputs=>Native.AllowMultipleInputs;
        public CommandLinkData predecessors {get=>Native.predecessors;set{var a=Native;a.predecessors=value;a.PushItself();}}
        public string OverTextIndirect=>"Antenna (bind Wireless Receivers)";
        public int LastFrameCheckedForCircularLink {get;set;}
        public bool AllowMultipleOutputs=>true;
        public bool CanBeLinkSource=>true;
        public CommandLinkData targets {get;set;}
        public bool CanLinkToCommand(ICommand target)=>target is WirelessReceiver;
        public bool CanBeLinkTargetFromCommand(ICommand predecessor)=>Native.CanBeLinkTargetFromCommand(predecessor);
        public void Activate(bool manualActivation,PlayerCommandManager player,float state){var a=Native;a.Activate(manualActivation,player,state);}
        public void OnEdited(){var a=Native;a.OnEdited();}
        public void OnStateUpdated(bool onLoad){var a=Native;a.OnStateUpdated(onLoad);}
        public void PushItself(){}
        public void OnTargetAdded(ICommand target,bool onLoad){}
        public void OnTargetRemoved(ICommand target,bool onUnload){}
    }
    internal static class AntennaBindings
    {
        internal static bool Match(ICommandLinkSource source,ICommand target,out WirelessRelay relay,out BrickCoord antenna)
        {
            relay=null;antenna=default;
            if(!RadioPolicy.IsBinding(source is WirelessTransmitter,source is AntennaLinkSource,target is WirelessReceiver,target is AntennaCommandData))return false;
            if(source is WirelessTransmitter tx && target is AntennaCommandData native){relay=tx;antenna=native.brickCoord;return true;}
            if(source is AntennaLinkSource adapter && target is WirelessReceiver rx){relay=rx;antenna=adapter.brickCoord;return true;}
            return false;
        }
    }
    [HarmonyPatch(typeof(CommandCommon),nameof(CommandCommon.CanBeLinkSource),new[]{typeof(ICommand)})]
    internal static class AntennaSourcePatch
    {private static void Postfix(ICommand command,ref bool __result){if(command is AntennaCommandData)__result=true;}}
    [HarmonyPatch(typeof(CommandCommon),nameof(CommandCommon.CanBeLinkSource),new[]{typeof(ICommandLinkSource)})]
    internal static class AntennaSourceTypedPatch
    {private static void Postfix(ICommandLinkSource command,ref bool __result){if(command is AntennaLinkSource)__result=true;}}
    [HarmonyPatch(typeof(ActionLink),nameof(ActionLink.LeftClickDown),new Type[0])]
    internal static class AntennaSelectionPatch
    {
        private static void Postfix()
        {
            if(ActionLink.IsLinking.Value && ActionLink.CommandSelected.Value is AntennaCommandData antenna)
                ActionLink.CommandSelected.Value=new AntennaLinkSource(antenna.brickCoord);
        }
    }
    [HarmonyPatch(typeof(CommandCommon),nameof(CommandCommon.CanLinkToCommand),new[]{typeof(ICommandLinkSource),typeof(ICommand)})]
    internal static class BindingAllowedPatch
    {
        private static bool Prefix(ICommandLinkSource command,ICommand target,ref bool __result)
        {
            if(!AntennaBindings.Match(command,target,out var relay,out var antenna))return true;
            __result=RadioNetwork.CanBind(relay,antenna);return false;
        }
    }
    [HarmonyPatch(typeof(CommandCommon),nameof(CommandCommon.LinkToCommand),new[]{typeof(ICommandLinkSource),typeof(ICommand),typeof(bool),typeof(bool)})]
    internal static class BindingCommitPatch
    {
        private static bool Prefix(ICommandLinkSource command,ICommand target,ref ICommandLinkSource __result)
        {
            if(!AntennaBindings.Match(command,target,out var relay,out var antenna))return true;
            if(RadioNetwork.CanBind(relay,antenna))RadioNetwork.Bind(relay,antenna,true);
            __result=command;return false;
        }
    }
}
