using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RadioLogic;

if(args.Length<2)throw new ArgumentException("dump|version|check|self-test <Assembly-CSharp.dll> [RadioLogic.dll]");
if((args[0]=="check"||args[0]=="self-test")&&args.Length<3)throw new ArgumentException("RF consumer DLL is required for complete native dependency validation");
using var resolver=new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
using var assembly=AssemblyDefinition.ReadAssembly(args[1],new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
var module=assembly.MainModule;
Console.WriteLine("Native identity (diagnostic only): "+module.Assembly.Name+" MVID="+module.Mvid);
if(args[0]=="version")
{
    foreach(var type in module.Types)
        foreach(var method in type.Methods.Where(m=>m.HasBody))
            foreach(var instruction in method.Body.Instructions)
                if(instruction.OpCode.Code==Code.Ldstr&&instruction.Operand is string s&&System.Text.RegularExpressions.Regex.IsMatch(s,@"26w\d\d[a-z]"))Console.WriteLine(method.FullName+": "+s);
    foreach(var t in module.GetType("PlayerState").NestedTypes.Where(t=>t.Name=="SessionMode"))
        foreach(var f in t.Fields.Where(f=>f.HasConstant))Console.WriteLine("ENUM "+f.FullName+"="+f.Constant);
    return;
}
using var consumer=args.Length>2?AssemblyDefinition.ReadAssembly(args[2]):null;
ContractReport Evaluate()=>NativeContracts.Check(module,consumer?.MainModule);
if(args[0]=="dump")
{
    foreach(var t in module.Types.Where(t=>new[]{"Brick","Command","CommandCommon","ResourcesLoader","VoxelMaterial","TabLogic","MaterialDynamicData","GameloopManager","ActionLink","AntennaCommandData","UI_CommandGeneric","CommandLinkData","BrickCoord","PlayerState","CommandInfoProperty","CommandCustomizableField","ICommand","ICommandLinkSource"}.Contains(t.Name)))
    {
        Console.WriteLine("TYPE "+t.FullName+" : "+t.BaseType);
        foreach(var f in t.Fields)Console.WriteLine(" FIELD "+f.FullName+" static="+f.IsStatic);
        foreach(var m in t.Methods)Console.WriteLine(" METHOD "+m.FullName+" static="+m.IsStatic+" virtual="+m.IsVirtual);
    }
    return;
}
int checks=0;
void Assert(bool ok,string label){checks++;Console.WriteLine((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);}
void Print(ContractReport r)
{
    foreach(var e in r.CoreErrors)Console.WriteLine("CORE "+e);
    foreach(var e in r.BindingErrors)Console.WriteLine("BINDINGS "+e);
    foreach(var e in r.MenuErrors)Console.WriteLine("MENU "+e);
    Console.WriteLine($"core={r.Core} bindings={r.Bindings} menu={r.Menu}");
}
var baseline=Evaluate();Print(baseline);
if(args[0]=="check"){Environment.ExitCode=baseline.Core&&baseline.Bindings&&baseline.Menu?0:1;return;}
Assert(baseline.Core&&baseline.Bindings&&baseline.Menu,"actual baseline satisfies required contracts");
var originalId=module.Mvid;var originalVersion=assembly.Name.Version;var originalName=assembly.Name.Name;
module.Mvid=Guid.NewGuid();assembly.Name.Version=new Version(99,88,77,66);
assembly.Name.Name="UnrelatedIdentityChange";
var changedIdentity=Evaluate();
if(!changedIdentity.Core||!changedIdentity.Bindings||!changedIdentity.Menu)Print(changedIdentity);
Assert(changedIdentity.Core&&changedIdentity.Bindings&&changedIdentity.Menu,"different MVID and assembly identity retain compatible features");
using(var rewritten=new MemoryStream())
{
    module.Write(rewritten);var bytes=rewritten.ToArray();
    bool changedHash=!System.Security.Cryptography.SHA256.HashData(bytes).SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[1])));
    using var reread=AssemblyDefinition.ReadAssembly(new MemoryStream(bytes),new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
    var binaryResult=NativeContracts.Check(reread.MainModule,consumer?.MainModule);
    Assert(changedHash&&binaryResult.Core&&binaryResult.Bindings&&binaryResult.Menu,"different whole-assembly hash with unchanged contracts passes after binary roundtrip");
}
module.Mvid=originalId;assembly.Name.Version=originalVersion;assembly.Name.Name=originalName;
var unrelated=new TypeDefinition("CompatibilityTests","UnrelatedFutureFeature",TypeAttributes.Class|TypeAttributes.Public,module.TypeSystem.Object);
module.Types.Add(unrelated);
Assert(Evaluate().Core,"unrelated type addition does not reject RF");
module.Types.Remove(unrelated);
void BrokenMethod(string type,string name,Action<ContractReport> verify)
{
    var owner=module.Types.Single(t=>t.FullName==type);var m=owner.Methods.First(m=>m.Name==name);
    string previous=m.Name;m.Name="ChangedDependency";var result=Evaluate();verify(result);m.Name=previous;
}
BrokenMethod("VoxelEngine.Brick","LoadAllResourcesRuntime",r=>Assert(!r.Core,"missing registration hook disables core"));
BrokenMethod("VoxelEngine.CommandCommon","LinkToCommand",r=>Assert(r.Core&&!r.Bindings&&r.Menu,"binding signature break disables only bindings"));
BrokenMethod("UI.UI_CommandGeneric","OpenConfig",r=>Assert(r.Core&&r.Bindings&&!r.Menu,"menu signature break disables only configuration"));
BrokenMethod("ResourcesLoader","GenerateIcon",r=>Assert(r.Core&&!r.Bindings&&r.Menu,"missing icon-ready hook disables only binding feature"));
var solo=module.GetType("PlayerState/SessionMode").Fields.Single(f=>f.Name=="solo");var originalSolo=solo.Constant;solo.Constant=2;
Assert(!Evaluate().Core,"changed solo enum value cannot accidentally admit multiplayer");solo.Constant=originalSolo;
var menuAttribute=module.GetType("UI.CommandCustomizableFieldBool").Methods.First(m=>m.IsConstructor&&!m.IsStatic);
var originalAttributeName=menuAttribute.Name;menuAttribute.Name="ChangedAttributeConstructor";
var changedAttribute=Evaluate();Assert(changedAttribute.Core&&!changedAttribute.Menu,"changed reflected menu attribute constructor disables only menu");menuAttribute.Name=originalAttributeName;
if(consumer!=null)
{
    BrokenMethod("VoxelEngine.Command","OnPredecessorRemoved",r=>Assert(!r.Core,"overridden-only inherited command removal disables core"));
    BrokenMethod("VoxelEngine.Command","OnTargetAdded",r=>Assert(!r.Core,"receiver inherited override removal disables core"));
    var activate=module.GetType("VoxelEngine.Command").Methods.Single(m=>m.Name=="Activate");
    var activateAttributes=activate.Attributes;activate.IsFinal=true;
    Assert(!Evaluate().Core,"final inherited Activate disables core");activate.Attributes=activateAttributes;
    var initialize=module.GetType("VoxelEngine.Command").Methods.Single(m=>m.Name=="OnInitialize");
    var initializeAttributes=initialize.Attributes;initialize.IsPrivate=true;
    Assert(!Evaluate().Core,"protected override becoming private disables core");initialize.Attributes=initializeAttributes;
    var setDirty=module.GetType("VoxelEngine.ParentEntity").Methods.First(m=>m.Name=="SetDirty");
    var dirtyAttributes=setDirty.Attributes;setDirty.IsPrivate=true;
    Assert(!Evaluate().Core,"native method becoming private disables core");setDirty.Attributes=dirtyAttributes;
    var brick=module.GetType("VoxelEngine.Brick");var load=brick.Methods.Single(m=>m.Name=="LoadResourcesRuntime");
    var loadAttributes=load.Attributes;load.IsFinal=true;
    Assert(!Evaluate().Core,"inherited RelayBrick resource override cannot become final");load.Attributes=loadAttributes;
    var commandBrick=module.Types.Single(t=>t.Name=="CommandBrick`1");var brickAttributes=commandBrick.Attributes;commandBrick.IsSealed=true;
    Assert(!Evaluate().Core,"sealed native relay brick base disables core");commandBrick.Attributes=brickAttributes;
    var iface=module.GetType("VoxelEngine.ICommandLinkSource");
    var added=new MethodDefinition("NewRequiredAbstractInput",MethodAttributes.Public|MethodAttributes.Abstract|MethodAttributes.Virtual|MethodAttributes.NewSlot,module.TypeSystem.Void);
    iface.Methods.Add(added);
    var changedInterface=Evaluate();Assert(changedInterface.Core&&!changedInterface.Bindings,"new required adapter interface member disables only binding feature");iface.Methods.Remove(added);
}
var complete=module.Types.Single(t=>t.FullName=="Manager.GameloopManager").Methods.Single(m=>m.Name=="OnAfterRendering");
var call=complete.Body.Instructions.First(i=>i.Operand is MethodReference m&&m.Name=="CompleteJobs");
var originalOperand=call.Operand;call.Operand=complete;
Assert(!Evaluate().Core,"missing completed-jobs boundary disables transport");call.Operand=originalOperand;
var originalCode=call.OpCode;call.OpCode=OpCodes.Ldftn;
Assert(!Evaluate().Core,"CompleteJobs token without invocation is not a completion boundary");call.OpCode=originalCode;
var execute=complete.Body.Instructions.First(i=>i.Operand is MethodReference m&&m.Name=="ExecuteCommandsJob");
var bypass=Instruction.Create(OpCodes.Br,execute);complete.Body.Instructions.Insert(0,bypass);
Assert(!Evaluate().Core,"branch bypassing job completion disables transport");complete.Body.Instructions.Remove(bypass);
var handler=new ExceptionHandler(ExceptionHandlerType.Catch){TryStart=complete.Body.Instructions[0],TryEnd=execute,HandlerStart=execute,HandlerEnd=execute.Next,CatchType=module.TypeSystem.Object};
complete.Body.ExceptionHandlers.Add(handler);
Assert(!Evaluate().Core,"unvalidated exception path into command execution disables transport");complete.Body.ExceptionHandlers.Remove(handler);
var overloadOwner=module.GetType("VoxelEngine.CommandCommon");
var harmlessOverload=new MethodDefinition("CanLinkToCommand",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Boolean);
harmlessOverload.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));harmlessOverload.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));harmlessOverload.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
overloadOwner.Methods.Add(harmlessOverload);
Assert(Evaluate().Core&&Evaluate().Bindings,"new unrelated overload preserves exact supported hook");overloadOwner.Methods.Remove(harmlessOverload);
var dyn=module.Types.Single(t=>t.Name=="MaterialDynamicData").Fields.Single(f=>f.Name=="FloatDynDatas");
var originalType=dyn.FieldType;dyn.FieldType=module.TypeSystem.Int32;
Assert(!Evaluate().Core,"changed dynamic-material storage layout disables RF");dyn.FieldType=originalType;
Assert(Evaluate().Core,"restored native contracts pass after mutations");
Console.WriteLine($"{checks} compatibility checks passed");
