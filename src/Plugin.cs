using System;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Manager;
using UI.Codex;
using UnityEngine;
using VoxelEngine;
using Mono.Cecil;

namespace RadioLogic
{
    [BepInPlugin("local.sevo.radiologic", "Wireless Relays (Unity 6 proof of concept)", "0.2.3")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static readonly object[] Materials = { "RF stock material reference" };
        internal static bool Registered;
        internal const int TxId = 1004, RxId = 1005;
        internal static string Root;
        private static Harmony harmony;
        private static bool bindingsInstalled;
        internal static bool CoreReady,MenuReady,BindingContractsReady;
        private void Awake()
        {
            Log = Logger;
            Root = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "..", ".."));
            try
            {
                string nativePath=Path.Combine(Paths.ManagedPath,"Assembly-CSharp.dll");
                using(var resolver=new DefaultAssemblyResolver())
                {
                resolver.AddSearchDirectory(Paths.ManagedPath);
                using(var native=AssemblyDefinition.ReadAssembly(nativePath,new ReaderParameters{AssemblyResolver=resolver,InMemory=true}))
                using(var consumer=AssemblyDefinition.ReadAssembly(Info.Location))
                {
                    var report=NativeContracts.Check(native.MainModule,consumer.MainModule);
                    Log.LogInfo("RF_NATIVE_IDENTITY diagnostic-only mvid="+native.MainModule.Mvid+" unity="+Application.unityVersion);
                    using(var sha=SHA256.Create())using(var input=File.OpenRead(nativePath))
                        Log.LogInfo("RF_NATIVE_SHA256 diagnostic-only "+BitConverter.ToString(sha.ComputeHash(input)).Replace("-",""));
                    foreach(string reason in report.CoreErrors)Log.LogError("RF_CORE_DISABLED "+reason);
                    foreach(string reason in report.BindingErrors)Log.LogWarning("RF_BINDINGS_DISABLED "+reason);
                    foreach(string reason in report.MenuErrors)Log.LogWarning("RF_MENU_DISABLED "+reason);
                    Log.LogInfo("RF_CONTRACTS core="+report.Core+" bindings="+report.Bindings+" menu="+report.Menu);
                    if(!report.Core)return;
                    MenuReady=report.Menu;BindingContractsReady=report.Bindings;
                }
                }
                InstallCore();
            }
            catch(Exception error){CoreReady=false;Log.LogError("RF_CORE_DISABLED dependency validation/patch failure: "+error);harmony?.UnpatchSelf();}
        }
        private void InstallCore()
        {
            harmony=new Harmony("local.sevo.radiologic");
            // Patching CommandCommon before native tab initialization can force
            // TabLogic's resource-loading initializer during Unity bootstrap.
            foreach(var patch in new[]{typeof(RegistrationPatch),typeof(MaterialPatch),typeof(RadioTickPatch)})
                harmony.CreateClassProcessor(patch).Patch();
            CoreReady=true;
            if(BindingContractsReady)
                try{harmony.CreateClassProcessor(typeof(IconDiagnosticPatch)).Patch();}
                catch(Exception error){BindingContractsReady=false;Log.LogWarning("RF_BINDINGS_DISABLED icon-ready hook unavailable: "+error);}
            if(Environment.GetEnvironmentVariable("SEVO_RF_DEMO")=="1")
                try{InstallDemo();}catch(Exception error){Log.LogWarning("RF_DEMO_DISABLED optional fixture unavailable: "+error);}
            Log.LogInfo("RF prototype loaded; solo-only, explicit pairs, native radar preserved.");
        }
        private void InstallDemo()=>gameObject.AddComponent<Demo>();
        internal static void Register()
        {
            if(Registered||!CoreReady) return;
            // Resolve stock definitions only after native resource initialization.
            // Keep RF/save IDs stable; discover the stock antenna definition by API.
            if(Brick.BRICK_INDEX==null||Brick.BRICK_INDEX.Length<=RxId||Brick.BRICK_INDEX[TxId]!=null||Brick.BRICK_INDEX[RxId]!=null)
            {CoreReady=false;Log.LogError("RF_CORE_DISABLED relay IDs unavailable; no definition replaced");return;}
            Brick stock=TabLogic.LOGIC_GATE;
            if(stock==null||string.IsNullOrEmpty(stock.nameMesh)||stock.VoxelMaterials==null||stock.VoxelMaterials.Length==0
                ||stock.VoxelMesh==null||stock.VoxelMesh.Mesh==null||stock.VoxelMesh.Mesh.vertexCount==0
                ||TabSystem.ANTENNA==null||TabSystem.ANTENNA.brickID<0||TabSystem.ANTENNA.brickID>=Brick.BRICK_INDEX.Length
                ||!ReferenceEquals(Brick.BRICK_INDEX[TabSystem.ANTENNA.brickID],TabSystem.ANTENNA)
                ||!(TabSystem.ANTENNA is BrickCommandData<AntennaCommandData>))
            {CoreReady=false;Log.LogError("RF_CORE_DISABLED stock logic assets or saved antenna mapping unavailable; native definitions preserved");return;}
            RegisterOne<WirelessTransmitter>(TxId,"Wireless Relay Transmitter",new Color32(70,180,255,255));
            RegisterOne<WirelessReceiver>(RxId,"Wireless Relay Receiver",new Color32(255,170,50,255));
            Registered = true;
            Log.LogInfo("RF_BRICKS_REGISTERED transmitter=1004 receiver=1005");
        }
        internal static void InstallBindings()
        {
            if(CoreReady&&Registered&&BindingContractsReady&&!bindingsInstalled)
            {
                try
                {
                    foreach(var patch in new[]{typeof(AntennaSourcePatch),typeof(AntennaSourceTypedPatch),typeof(AntennaSelectionPatch),typeof(BindingAllowedPatch),typeof(BindingCommitPatch)})
                        harmony.CreateClassProcessor(patch).Patch();
                    bindingsInstalled=true;
                    Log.LogInfo("RF_BINDING_PATCHES_READY after native allocation, schematics and icon generation");
                }
                catch(Exception error)
                {
                    BindingContractsReady=false;
                    foreach(var method in typeof(CommandCommon).GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                        if(method.Name=="CanBeLinkSource"||method.Name=="CanLinkToCommand"||method.Name=="LinkToCommand")harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                    harmony.Unpatch(AccessTools.Method(typeof(ActionLink),nameof(ActionLink.LeftClickDown)),HarmonyPatchType.All,harmony.Id);
                    Log.LogError("RF_BINDINGS_DISABLED rolled back tool hooks; existing saved bindings retain transport. "+error);
                }
            }
        }
        private static void RegisterOne<T>(int id,string name,Color32 color) where T:Command,new()
        {
            Brick stock=TabLogic.LOGIC_GATE;
            var brick=new RelayBrick<T>(id,name,stock) {
                defaultPaint=color,PreviewPaint=color,functionType=BrickFunctionType.control,
                massForUnitShape=25,gridLevel=1,gridLockOffset=stock.gridLockOffset,
                boundsOverride=stock.boundsOverride,hasGlowPaint=true,cost=8
            };
            brick.Desc("Pair named relays in configuration. Link transmitter -> antenna, antenna -> receiver with the link tool. Both antennas need an ON signal from a switch or logic input. Antennas carry independent signals. Solo prototype.").Tab(CodexTab.COMMANDS);
            brick.LoadAfterStock();
        }
    }
    internal sealed class RelayBrick<T>:CommandBrick<T> where T:Command,new()
    {
        internal RelayBrick(int id,string name,Brick stock):base(id,name,stock.nameMesh,ResizeMode.NoneBrick,Plugin.Materials){}
        protected override void LoadResourcesRuntime(){}
        internal void LoadAfterStock()=>base.LoadResourcesRuntime();
    }
    [HarmonyPatch(typeof(Brick),nameof(Brick.LoadAllResourcesRuntime),new Type[0])]
    internal static class RegistrationPatch
    {
        private static void Postfix()
        {try{Plugin.Register();}catch(Exception error){Plugin.CoreReady=false;Plugin.Log.LogError("RF_CORE_DISABLED registration failed: "+error);}}
    }
    [HarmonyPatch(typeof(VoxelMaterial),nameof(VoxelMaterial.CreateFaceIDArrayRuntime),new[]{typeof(object[])})]
    internal static class MaterialPatch
    {
        private static bool Prefix(object[] __0,ref VoxelMaterial[] __result)
        { if(!ReferenceEquals(__0,Plugin.Materials))return true; __result=TabLogic.LOGIC_GATE.VoxelMaterials;return false; }
    }
    [HarmonyPatch(typeof(ResourcesLoader),nameof(ResourcesLoader.GenerateIcon),new Type[0])]
    internal static class IconDiagnosticPatch
    {
        private static void Postfix()
        {
            var atlas=Shader.GetGlobalTexture("ITEM_ICON_TEX") as Texture2DArray;
            Plugin.Log.LogInfo("RF_ICON_GENERATION atlas="+(atlas==null?"MISSING":atlas.name+" depth="+atlas.depth)+" items="+Item.CodexItems.Count);
            if(atlas!=null&&atlas.depth>=2*Item.CodexItems.Count)Plugin.InstallBindings();
            else Plugin.Log.LogWarning("RF_BINDINGS_WAITING native icon atlas unavailable or incomplete; native logic remains unchanged");
        }
    }
    [HarmonyPatch(typeof(GameloopManager),"ExecuteCommandsJob",new Type[0])]
    internal static class RadioTickPatch
    {
        private static void Postfix()
        {
            if(!Plugin.CoreReady||!Plugin.Registered)return;
            try{RadioNetwork.Tick();Demo.Step();}
            catch(Exception error)
            {
                try{RadioNetwork.FailClosed();}catch(Exception cleanup){Plugin.Log.LogError("RF_FAIL_CLOSED_FAILED "+cleanup);}
                Plugin.Log.LogError("RF_TICK_FAILED "+error);
            }
        }
    }
}
