using System;
using System.Collections.Generic;
using System.IO;
using Common.RNG;
using Game;
using Game.Player;
using Manager;
using Manager.Save;
using Mirror;
using UI;
using UnityEngine;
using VoxelEngine;

namespace RadioLogic
{
    // Diagnostic fixture only: admitted by launcher environment and fixed private identity.
    public sealed class Demo:MonoBehaviour
    {
        private const string Character="RadioLab";
        private static Demo current;
        private bool requested,worldReady,built,finished,quitRequested;
        private int stage,checks,failures;
        private float nextTime,createdAt;
        private ParentEntity ship,station,bystander;
        private WirelessTransmitter signalTx,pulseTx;
        private WirelessReceiver signalRx,pulseRx,unpairedRx;
        private ICommand input,pulseInput,signalLamp,pulseLamp,antenna,powerSwitch;
        private Vector3l nearPosition,farPosition;
        private string evidence;
        private string evidenceDirectory;
        private Action pendingUI;
        private void Awake()
        {
            string keelVersion=Environment.GetEnvironmentVariable("SEVO_RF_KEEL_TEST");
            string product=keelVersion switch
            {
                "0.1.6"=>"SEVO RF K016",
                "1"=>"SEVO RF Keel",
                _=>"SEVO RF Test"
            };
            string evidenceFolder=keelVersion switch
            {
                "0.1.6"=>"keel-0.1.6",
                "1"=>"keel-0.1.2",
                _=>""
            };
            if(Application.companyName!="SEVO Radios SandboxLab" || Application.productName!=product)throw new InvalidOperationException("Demo refuses non-RF profile");
            evidenceDirectory=Path.Combine(Plugin.Root,"local-evidence",evidenceFolder);
            Directory.CreateDirectory(evidenceDirectory);
            current=this;evidence=Path.Combine(evidenceDirectory,"runtime.tsv");
            if(File.Exists(evidence))File.Copy(evidence,evidence+"."+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),false);
            File.WriteAllText(evidence,"utc\tresult\tcheck\n");
            LoadManager.OnInitialLoadCompleted+=Ready;
        }
        private void Ready(){worldReady=true;Plugin.Log.LogInfo("RF_WORLD_READY "+SaveManager.CharacterName);}
        private void OnDestroy(){LoadManager.OnInitialLoadCompleted-=Ready;if(current==this)current=null;}
        private void Update()
        {
            // Native menus destroy/rebuild preview meshes. Open them during Update,
            // never from the command execution hook inside the rendering callback.
            if(pendingUI!=null && !GameloopManager.jobStarted)
            {var action=pendingUI;pendingUI=null;action();}
            // ResourcesLoader invokes icon generation after Awake. Scene changes before
            // that invocation destroy its camera/generator and leave every Codex tile white.
            var icons=Shader.GetGlobalTexture("ITEM_ICON_TEX") as Texture2DArray;
            if(!requested && icons!=null && icons.depth>=2*Item.CodexItems.Count && Time.realtimeSinceStartup>5 && Plugin.Registered && UI_TitleScreen.IsTitleScene && NetworkManager.singleton!=null && !NetworkClient.active && !NetworkServer.active)
            {
                requested=true;
                Plugin.Log.LogInfo("RF_CODEX_READY atlas="+icons.name+" depth="+icons.depth+" items="+Item.CodexItems.Count);
                string path=PlayerSave_mp.GetPath(Character);
                PlayerState.SetGameMode(PlayerState.GameMode.creative);PlayerState.SetSessionMode(PlayerState.SessionMode.solo);
                if(File.Exists(path))SaveManager.SetCharacter(Character,PlayerSave_mp.LoadFromDisk(path));
                else SaveManager.NewCharacter(Character,"RADIOPOC",PlayerState.GameMode.creative,SkinDataCommon.GetRandomSkin(SkeetoHash.GetRandomSeed()));
                NetworkServer.dontListen=true;NetworkManager.singleton.StartHost();
                Plugin.Log.LogInfo("RF_WORLD_REQUESTED character=RadioLab new="+!File.Exists(path));
            }
            if(File.Exists(Path.Combine(evidenceDirectory,"quit.request")))quitRequested=true;
            if(quitRequested && worldReady && !PlayerState.InLoadingScreen && !GameloopManager.jobStarted)
            {
                foreach(var menu in Resources.FindObjectsOfTypeAll<UI_MainMenu>())
                    if(menu.gameObject.scene.IsValid()){quitRequested=false;menu.QuitApplication();break;}
            }
        }
        internal static void Step(){if(current!=null)current.Tick();}
        private void Tick()
        {
            if(!worldReady || PlayerState.InLoadingScreen || SaveManager.CharacterName!=Character || finished || pendingUI!=null || Time.time<nextTime)return;
            if(!built)
            {
                if(Dependency.PlayerManager==null)return;
                built=true;createdAt=Time.time;
                // A saved fixture is loaded for inspection without automatic rebuilding or retesting.
                var saved=Path.Combine(evidenceDirectory,"fixture.txt");
                if(File.Exists(saved))
                {
                    foreach(string line in File.ReadAllLines(saved))
                    {
                        if(line.StartsWith("Ship="))ship=BlockEntityManager.instance.GetEntityBySGUID(line.Substring(5));
                        if(line.StartsWith("Base="))station=BlockEntityManager.instance.GetEntityBySGUID(line.Substring(5));
                        if(line.StartsWith("Bystander="))bystander=BlockEntityManager.instance.GetEntityBySGUID(line.Substring(10));
                    }
                    if(ship!=null && station!=null && bystander!=null)
                    {
                        ship.EntityName="RF Demo Ship";station.EntityName="RF Demo Base";bystander.EntityName="RF Unpaired Friendly";
                        nearPosition=ship.SkyTransform.SystemPosition;farPosition=nearPosition+new Vector3l(new Vector3(0,0,200));
                        if(Environment.GetEnvironmentVariable("SEVO_RF_RETEST")!="1")
                        {finished=true;Plugin.Log.LogInfo("RF_EXISTING_WORLD preserved; relay count="+RadioNetwork.Relays.Count);return;}
                        nextTime=Time.time+1;return;
                    }
                }
                nearPosition=Dependency.PlayerManager.SkyTransform.SystemPosition+new Vector3l(new Vector3(12,4,12));
                farPosition=nearPosition+new Vector3l(new Vector3(0,0,200));
                ship=CreateGrid("RF Demo Ship",nearPosition,true,false);
                station=CreateGrid("RF Demo Base",nearPosition+new Vector3l(new Vector3(0,0,15)),false,false);
                bystander=CreateGrid("RF Unpaired Friendly",nearPosition+new Vector3l(new Vector3(10,0,15)),false,true);
                nextTime=Time.time+2;return;
            }
            if(ship==null || station==null || !ship.IsInitialized || !station.IsInitialized || bystander==null || !bystander.IsInitialized)
            {if(Time.time-createdAt>60)throw new InvalidOperationException("RF fixture grid initialization timeout");return;}
            switch(stage++)
            {
                case 0:
                    signalTx=(WirelessTransmitter)Command(ship,26);pulseTx=(WirelessTransmitter)Command(ship,27);
                    signalRx=(WirelessReceiver)Command(station,26);pulseRx=(WirelessReceiver)Command(station,27);unpairedRx=(WirelessReceiver)Command(bystander,26);
                    input=Command(ship,28);pulseInput=Command(ship,29);antenna=Command(ship,25);
                    signalLamp=Command(station,30);pulseLamp=Command(station,31);
                    if(input.State>.5f)input.Activate(false,null,1);
                    if(pulseInput.State>.5f)pulseInput.Activate(false,null,0);
                    RadioNetwork.Pair(signalTx,signalRx,25,25);RadioNetwork.Pair(pulseTx,pulseRx,25,25);pulseTx.Settings.Pulse=true;
                    int originalControlInputs=Command(ship,25).predecessors.Count;
                    RadioNetwork.ClearAntenna(signalTx);RadioNetwork.ClearAntenna(pulseTx);
                    RadioNetwork.ClearAntenna(signalRx);RadioNetwork.ClearAntenna(pulseRx);
                    Check(CommandCommon.CanBeLinkSource(antenna),"native antenna can be selected as RF link source");
                    Link(signalTx,antenna);Link(pulseTx,antenna);
                    var baseAntenna=Command(station,25);
                    var antennaSource=new AntennaLinkSource(baseAntenna.brickCoord);
                    Link(antennaSource,signalRx);Link(antennaSource,pulseRx);
                    Check(signalTx.Settings.AntennaBrick==25 && pulseTx.Settings.AntennaBrick==25 && signalRx.Settings.AntennaBrick==25 && pulseRx.Settings.AntennaBrick==25,
                        "native link commit binds several independent relays to each antenna");
                    Check(Command(ship,25).predecessors.Count==originalControlInputs,"RF payload bindings do not consume antenna activation inputs");
                    for(int powerId=30;powerId<ship.Bricks.brickDatas.Length;powerId++)
                    {
                        var data=ship.Bricks.brickDatas[powerId];
                        if(data.IsValid && data.brickId==TabLogic.SWITCH_BUTTON.brickID){powerSwitch=Command(ship,powerId);break;}
                    }
                    if(powerSwitch==null)
                    {
                        int id=ship.Bricks.Add(Data(TabLogic.SWITCH_BUTTON,0,new Vector3i(160,32,32),1));
                        powerSwitch=Command(ship,id);
                    }
                    ((Command)powerSwitch).CommandLabel="Antenna power";
                    Link((ICommandLinkSource)powerSwitch,Command(ship,25));
                    Check(Command(ship,25).predecessors.Count==1,"antenna accepts its normal power-control link alongside multiple RF bindings");
                    Link((ICommandLinkSource)input,signalTx);Link((ICommandLinkSource)pulseInput,pulseTx);
                    Link(signalRx,Command(station,28));Link((ICommandLinkSource)Command(station,28),signalLamp);
                    Link(pulseRx,Command(station,29));Link((ICommandLinkSource)Command(station,29),pulseLamp);
                    Check(!CommandCommon.CanLinkToCommand((ICommandLinkSource)input,signalRx),"native cross-grid link restriction preserved");
                    WriteFixture();break;
                case 1:
                    // Linking uses native command-save RPCs. Let that initial
                    // saved switch state arrive before driving the new control.
                    if(powerSwitch.State<.5f)powerSwitch.Activate(false,null,1);
                    Check(powerSwitch.State==1 && Command(ship,25).State==1,"native power control initialized after link save propagation");
                    input.Activate(false,null,1);break;
                case 2:
                    Check(signalRx.Connected && signalRx.State==1 && signalLamp.State==1,"native input -> RF -> native OR gate -> indicator ON");
                    Check(unpairedRx.State==0 && !unpairedRx.Connected && pulseRx.State==0,"friendly bystander and independent pulse pair remain OFF");
                    input.Activate(false,null,1);break;
                case 3: Check(signalRx.State==0 && signalLamp.State==0,"native sustained OFF propagates");pulseInput.Activate(false,null,1);break;
                case 4: Check(pulseRx.DeliveredPulses==1 && pulseRx.State==1 && pulseLamp.State==1,"one rising edge produces one native timed pulse");pulseInput.Activate(false,null,0);nextTime=Time.time+.4f;return;
                case 5: Check(pulseRx.State==0 && pulseLamp.State==0,"pulse returns native output to OFF");input.Activate(false,null,1);break;
                case 6: ship.SkyTransform.AddCommand(farPosition,Quaternion.identity);break;
                case 7: Check(!signalRx.Connected && signalRx.State==0 && signalLamp.State==0,"moving actual grid beyond level-1 range forces OFF");pulseInput.Activate(false,null,1);break;
                case 8: pulseInput.Activate(false,null,0);ship.SkyTransform.AddCommand(nearPosition,Quaternion.identity);break;
                case 9:
                    Check(signalRx.Connected && signalRx.State==1 && signalLamp.State==1,"reconnection restores current sustained ON");
                    Check(pulseRx.DeliveredPulses==1 && pulseRx.State==0,"disconnected pulse discarded without replay");
                    powerSwitch.Activate(false,null,1);break;
                case 10:
                    Check(signalRx.State==0 && !signalRx.Connected && signalLamp.State==0,"native antenna power switch forces receiver and native output OFF");
                    Check(!pulseRx.Connected,"shared antenna OFF disconnects every RF channel");
                    powerSwitch.Activate(false,null,1);break;
                case 11:
                    Check(signalRx.State==1 && signalLamp.State==1,"antenna reactivation restores sustained state");
                    Check(signalTx.GetCustomData(true).Length>0,"relay participates in native custom-data export");
                    pendingUI=()=>
                    {
                        signalTx.OpenSettings();
                        bool hidden=true;
                        foreach(string field in new[]{"Endpoint","Peer","AntennaBrick","AntennaChild"})
                            hidden &= typeof(RelaySettings).GetProperty(field).GetCustomAttributes(typeof(BaseCommandAttribute),true).Length==0;
                        Check(hidden && signalTx.Settings.PairOptions.Contains(PairLabel(signalRx)),"native pairing UI lists named receivers and hides UUIDs/antenna IDs");
                        signalTx.Settings.UnpairAction();UI_CommandGeneric.CloseWindow();
                    };break;
                case 12:
                    Check(!signalRx.Connected && signalRx.State==0 && signalLamp.State==0,"Unpair UI action disconnects both endpoints and turns native output OFF");
                    pendingUI=()=>
                    {
                        signalTx.OpenSettings();
                        signalTx.Settings.SelectedPair=signalTx.Settings.PairOptions.IndexOf(PairLabel(signalRx));
                        signalTx.Settings.PairAction();UI_CommandGeneric.CloseWindow();
                    };break;
                case 13:
                    Check(signalTx.Settings.Peer==signalRx.Endpoint && signalRx.Settings.Peer==signalTx.Endpoint && signalLamp.State==1 && signalTx.State==input.State,
                        "named receiver choice and Pair UI action establish both ends without UUID entry");
                    var bytes=signalRx.GetCustomData(false);signalRx.OnSaveLoaded(bytes,false);
                    RadioNetwork.ClearAntenna(signalRx);break;
                case 14:
                    Check(signalRx.State==0 && signalLamp.State==0,"receiver load synchronization clears stale downstream state before revalidation");
                    Link(new AntennaLinkSource(Command(station,25).brickCoord),signalRx);break;
                case 15:
                    Check(signalRx.Connected && signalLamp.State==1,"link-tool antenna rebinding restores the paired sustained signal");
                    ship.SetDirty();station.SetDirty();bystander.SetDirty();
                    finished=true;
                    Plugin.Log.LogInfo("RF_DEMO_COMPLETE checks="+checks+" failures="+failures);
                    File.WriteAllText(Path.Combine(evidenceDirectory,"demo-result.txt"),"checks="+checks+" failures="+failures+"\nNative runtime evidence: runtime.tsv\n");
                    if(failures==0)pendingUI=()=>
                    {
                        if(Environment.GetEnvironmentVariable("SEVO_RF_INSPECT_RECEIVER")=="1")signalRx.OpenSettings();
                        else signalTx.OpenSettings();
                    };
                    break;
            }
            nextTime=Time.time+.1f;
        }
        private static string PairLabel(WirelessRelay relay)=>relay.FriendlyName+" ("+relay.brickData.Position.ToString("F1")+")";
        private void WriteFixture()
        {
            File.WriteAllText(Path.Combine(evidenceDirectory,"fixture.txt"),
                "Character=RadioLab\nShip="+ship.SGUID+"\nBase="+station.SGUID+"\nBystander="+bystander.SGUID+
                "\nSignal TX="+signalTx.Endpoint+"\nSignal RX="+signalRx.Endpoint+"\nPulse TX="+pulseTx.Endpoint+"\nPulse RX="+pulseRx.Endpoint+"\nAntenna instance ID=25 on each parent grid\n");
        }
        private void Check(bool ok,string name)
        {
            checks++;if(!ok)failures++;
            File.AppendAllText(evidence,DateTime.UtcNow.ToString("O")+"\t"+(ok?"PASS":"FAIL")+"\t"+name+"\n");
            Plugin.Log.LogInfo("RF_CHECK "+(ok?"PASS ":"FAIL ")+name);
            if(!ok && signalTx!=null)
                Plugin.Log.LogWarning("RF_DIAGNOSTIC reason="+signalTx.Reason+" source="+input?.State+" tx="+signalTx.State+" rx="+signalRx?.State+" lamp="+signalLamp?.State+" pulseCount="+pulseRx?.DeliveredPulses);
        }
        private static void Link(ICommandLinkSource a,ICommand b)
        {
            if(a.targets.Contains(b))return;
            if(!CommandCommon.CanLinkToCommand(a,b))throw new InvalidOperationException("Invalid fixture local link "+a.OverTextIndirect+" -> "+b.OverTextIndirect);
            CommandCommon.LinkToCommand(a,b,false,false);
        }
        private static ICommand Command(ParentEntity grid,int id)=>grid.Bricks.brickDatas[id].Brick.GetCommand(grid.Bricks.brickIds[id]);
        private static ParentEntity CreateGrid(string name,Vector3l position,bool transmitter,bool unpaired)
        {
            var grid=BlockEntityManager.instance.CreateEntity(new WorldTransform(position,Quaternion.identity),Dependency.PlayerManager.SkyTransform.CurrentSystem);
            grid.EntityName=name;
            grid.SetType(EntityType.terrainBuilding);grid.SetBuildPermissionWithChildren(BuildPermission.canBeBuilt);
            grid.EntityMetaData.name=name;grid.EntityMetaData.description="Wireless Relay solo proof of concept";
            var bricks=new List<BrickInstanceData>{default};
            for(int z=0;z<3;z++)for(int x=0;x<8;x++)bricks.Add(Data(TabHull.HULL_A,bricks.Count,new Vector3i(x*32,0,z*32),3));
            bricks.Add(Data(TabSystem.ANTENNA,25,new Vector3i(0,32,0),1));
            bricks.Add(Data(Brick.BRICK_INDEX[transmitter?Plugin.TxId:Plugin.RxId],26,new Vector3i(32,32,0),1));
            bricks.Add(Data(Brick.BRICK_INDEX[transmitter?Plugin.TxId:Plugin.RxId],27,new Vector3i(64,32,0),1));
            bricks.Add(Data(transmitter?TabLogic.SWITCH_BUTTON:TabLogic.LOGIC_GATE,28,new Vector3i(96,32,0),1));
            bricks.Add(Data(transmitter?TabLogic.BUTTON:TabLogic.LOGIC_GATE,29,new Vector3i(128,32,0),1));
            if(!transmitter){bricks.Add(Data(TabLogic.COMMAND_INDICATOR,30,new Vector3i(160,32,0),1));bricks.Add(Data(TabLogic.COMMAND_INDICATOR,31,new Vector3i(192,32,0),1));}
            var save=new BrickEntity_mp{entityName=name,entityType=(int)EntityType.terrainBuilding,version=12,
                brickDatas=new BrickDatasSave{datas=bricks.ToArray(),idsToRecycle=Array.Empty<int>(),additionalDatas=new Dictionary<int,byte[]>()}};
            save.LoadInto(grid);grid.SetDirty();return grid;
        }
        private static BrickInstanceData Data(Brick brick,int id,Vector3i position,byte level)=>new BrickInstanceData{
            brickId=(ushort)brick.brickID,instanceId=id,gridPosition=position,gridLevel=level,healthScore=255,color=brick.defaultPaint};
    }
}
