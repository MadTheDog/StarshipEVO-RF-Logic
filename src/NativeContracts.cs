using System.Collections.Generic;
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RadioLogic
{
    internal sealed class ContractReport
    {
        internal readonly List<string> CoreErrors=new List<string>();
        internal readonly List<string> BindingErrors=new List<string>();
        internal readonly List<string> MenuErrors=new List<string>();
        internal bool Core=>CoreErrors.Count==0;
        internal bool Bindings=>BindingErrors.Count==0;
        internal bool Menu=>MenuErrors.Count==0;
    }
    internal static class NativeContracts
    {
        // Deliberately compares native contracts, never build IDs, hashes or MVIDs.
        // Cecil reads metadata/IL without invoking Unity or TabLogic initializers.
        internal static ContractReport Check(ModuleDefinition module,ModuleDefinition consumer=null)
        {
            var report=new ContractReport();
            var core=report.CoreErrors;var binding=report.BindingErrors;var menu=report.MenuErrors;
            var sessions=module.GetType("PlayerState/SessionMode");
            if(sessions==null||!sessions.IsEnum||!sessions.Fields.Any(f=>f.Name=="solo"&&f.HasConstant&&Convert.ToInt32(f.Constant)==1)
                ||sessions.Fields.Any(f=>f.Name!="solo"&&f.HasConstant&&Convert.ToInt32(f.Constant)==1))core.Add("Solo session enum contract changed; refuse possible multiplayer admission");
            M(module,core,"VoxelEngine.Brick","LoadAllResourcesRuntime","System.Void",true);
            M(module,core,"VoxelEngine.VoxelMaterial","CreateFaceIDArrayRuntime","VoxelEngine.VoxelMaterial[]",true,"System.Object[]");
            M(module,binding,"ResourcesLoader","GenerateIcon","System.Void",false);
            M(module,core,"Manager.GameloopManager","ExecuteCommandsJob","System.Void",true);
            M(module,core,"Manager.GameloopManager","OnAfterRendering","System.Void",true);
            M(module,core,"VoxelEngine.Command","Propagate","System.Void",false);
            M(module,core,"VoxelEngine.Command","OnStateUpdated","System.Void",false,"System.Boolean");
            M(module,core,"VoxelEngine.Command","GetCustomData","System.Byte[]",false,"System.Boolean");
            M(module,core,"VoxelEngine.Command","OnSaveLoaded","System.Void",false,"System.Byte[]","System.Boolean");
            M(module,core,"VoxelEngine.Command","OnInitialize","System.Void",false,"VoxelEngine.BrickIds","System.Boolean","System.Object[]");
            M(module,core,"VoxelEngine.CommandLinkData","get_Command","VoxelEngine.ICommand",false);
            M(module,core,"VoxelEngine.MaterialDynamicData","RegisterVector","VoxelEngine.DynamicDataHandle",true);
            M(module,core,"VoxelEngine.MaterialDynamicData","RegisterFloat","VoxelEngine.DynamicDataHandle",true,"System.Single");
            F(module,core,"VoxelEngine.MaterialDynamicData","FloatDynDatas","Common.NativeRecyclableContainer`1<System.Single>",true);
            F(module,core,"VoxelEngine.Brick","BRICK_INDEX","VoxelEngine.Brick[]",true);
            F(module,core,"VoxelEngine.TabLogic","LOGIC_GATE","VoxelEngine.Brick",true);
            var command=module.GetType("VoxelEngine.Command");
            if(command==null||command.IsValueType||command.IsSealed)core.Add("Command must remain an extensible reference type");
            var antenna=module.GetType("VoxelEngine.AntennaCommandData");
            if(antenna==null||!antenna.IsValueType)core.Add("AntennaCommandData value-type contract changed");
            // RF runs after native command work; require the completed-jobs boundary.
            OrderedCalls(module,core,"Manager.GameloopManager","OnAfterRendering",
                "Manager.GameloopManager::CompleteJobs","Manager.GameloopManager::ExecuteCommandsJob");
            CompletedBoundary(module,core);
            OrderedCalls(module,binding,"ResourcesLoader","Awake",
                "VoxelEngine.Brick::LoadAllResourcesRuntime","UnityEngine.MonoBehaviour::Invoke");
            RequireCall(module,binding,"ResourcesLoader","GenerateIcon","UnityEngine.Shader::SetGlobalTexture");
            var icon=module.GetType("ResourcesLoader")?.Methods.FirstOrDefault(m=>m.Name=="GenerateIcon");
            if(icon==null||!icon.HasBody||!icon.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldstr&&(string)i.Operand=="ITEM_ICON_TEX"))binding.Add("Native icon publication contract changed");
            RequireCall(module,core,"VoxelEngine.Command","Propagate","VoxelEngine.ICommand::Activate");
            M(module,binding,"VoxelEngine.CommandCommon","CanBeLinkSource","System.Boolean",true,"VoxelEngine.ICommand");
            M(module,binding,"VoxelEngine.CommandCommon","CanBeLinkSource","System.Boolean",true,"VoxelEngine.ICommandLinkSource");
            M(module,binding,"VoxelEngine.CommandCommon","CanLinkToCommand","System.Boolean",true,"VoxelEngine.ICommandLinkSource","VoxelEngine.ICommand");
            M(module,binding,"VoxelEngine.CommandCommon","LinkToCommand","VoxelEngine.ICommandLinkSource",true,"VoxelEngine.ICommandLinkSource","VoxelEngine.ICommand","System.Boolean","System.Boolean");
            M(module,binding,"VoxelEngine.ActionLink","LeftClickDown","System.Void",false);
            F(module,binding,"VoxelEngine.ActionLink","CommandSelected","Common.CommandEventVariable",true);
            F(module,binding,"VoxelEngine.ActionLink","IsLinking","Common.BoolEventVariable",true);
            M(module,menu,"UI.UI_CommandGeneric","OpenConfig","System.Void",true,"UI.UI_CommandGeneric/WindowsParams","System.Boolean");
            M(module,menu,"UI.CommandInfoProperty",".ctor","System.Void",false,"System.String","System.String","System.Boolean","System.Boolean");
            if(consumer!=null)CheckReferences(module,consumer,report);
            return report;
        }
        private static MethodDefinition M(ModuleDefinition module,List<string> errors,string type,string name,string returns,bool isStatic,params string[] parameters)
        {
            var candidates=module.GetType(type)?.Methods.Where(m=>m.HasBody&&m.Name==name&&m.ReturnType.FullName==returns&&m.IsStatic==isStatic&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
            if(candidates==null||candidates.Length!=1){errors.Add("Required signature: "+returns+" "+type+"::"+name+"("+string.Join(",",parameters)+")");return null;}
            return candidates[0];
        }
        private static void F(ModuleDefinition module,List<string> errors,string type,string name,string fieldType,bool isStatic)
        {
            if(module.GetType(type)?.Fields.Any(f=>f.Name==name&&f.FieldType.FullName==fieldType&&f.IsStatic==isStatic)!=true)
                errors.Add("Required field: "+fieldType+" "+type+"::"+name);
        }
        private static IEnumerable<string> Calls(ModuleDefinition module,string type,string name)
        {
            var method=module.GetType(type)?.Methods.FirstOrDefault(m=>m.Name==name);
            if(method==null||!method.HasBody)return Array.Empty<string>();
            return method.Body.Instructions.Where(i=>i.OpCode.Code==Code.Call||i.OpCode.Code==Code.Callvirt)
                .Select(i=>i.Operand).OfType<MethodReference>().Select(m=>m.DeclaringType.FullName+"::"+m.Name);
        }
        private static void OrderedCalls(ModuleDefinition module,List<string> errors,string type,string name,string first,string second)
        {
            var calls=Calls(module,type,name).ToList();int a=calls.IndexOf(first);
            if(a<0||!calls.Skip(a+1).Contains(second))errors.Add(type+"::"+name+" requires "+first+" before "+second);
        }
        private static void RequireCall(ModuleDefinition module,List<string> errors,string type,string name,string target)
        {
            if(!Calls(module,type,name).Contains(target))errors.Add(type+"::"+name+" no longer calls "+target);
        }
        private static void CompletedBoundary(ModuleDefinition module,List<string> errors)
        {
            var owner=module.GetType("Manager.GameloopManager");
            if(owner==null)return;
            foreach(var method in owner.Methods.Where(m=>m.HasBody))
            {
                var instructions=method.Body.Instructions;
                foreach(var target in instructions.Where(i=>IsCall(i,owner.FullName,"ExecuteCommandsJob")))
                {
                    // Unknown exception edges cannot be proven safe by the normal CFG.
                    if(method.Body.HasExceptionHandlers){errors.Add(method.FullName+" has unvalidated exception paths at the command boundary");continue;}
                    var pending=new Stack<Instruction>();var visited=new HashSet<Instruction>();pending.Push(instructions[0]);
                    while(pending.Count>0)
                    {
                        var current=pending.Pop();if(!visited.Add(current))continue;
                        if(current==target){errors.Add(method.FullName+" can execute RF before CompleteJobs");break;}
                        if(IsCall(current,owner.FullName,"CompleteJobs"))continue;
                        if(current.Operand is Instruction jump)pending.Push(jump);
                        if(current.Operand is Instruction[] jumps)foreach(var branch in jumps)pending.Push(branch);
                        var flow=current.OpCode.FlowControl;
                        if(flow!=FlowControl.Branch&&flow!=FlowControl.Return&&flow!=FlowControl.Throw&&current.Next!=null)pending.Push(current.Next);
                    }
                }
            }
        }
        private static bool IsCall(Instruction instruction,string type,string name)
            =>(instruction.OpCode.Code==Code.Call||instruction.OpCode.Code==Code.Callvirt)&&instruction.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name;
        // Cover every statically referenced game method/field in the shipped RF code,
        // including typed entity/save APIs. Optional UI/tool references are isolated.
        private static void CheckReferences(ModuleDefinition native,ModuleDefinition consumer,ContractReport report)
        {
            foreach(var type in consumer.Types)
            {
                if(type.Name=="Demo"||type.Name.StartsWith("<"))continue;
                var errors=type.Name=="RelaySettings"?report.MenuErrors:
                    type.Name.StartsWith("Antenna")||type.Name.StartsWith("Binding")||type.Name=="IconDiagnosticPatch"?report.BindingErrors:report.CoreErrors;
                if(type.BaseType!=null&&IsGame(type.BaseType))
                {
                    var parent=NativeType(native,type.BaseType);
                    if(parent==null||parent.IsSealed||!parent.IsClass||!PublicType(parent))errors.Add("Native base type changed: "+type.BaseType.FullName);
                }
                var attributes=type.CustomAttributes.Concat(type.Properties.SelectMany(p=>p.CustomAttributes))
                    .Concat(type.Fields.SelectMany(f=>f.CustomAttributes)).Concat(type.Methods.SelectMany(m=>m.CustomAttributes));
                foreach(var attribute in attributes.Where(a=>IsGame(a.Constructor.DeclaringType)))
                {
                    var owner=NativeType(native,attribute.Constructor.DeclaringType);
                    if(owner==null||!owner.Methods.Any(m=>m.IsConstructor&&m.IsPublic&&SameMethod(m,attribute.Constructor)))
                        errors.Add("Reflected native attribute constructor changed: "+attribute.Constructor.FullName);
                }
                foreach(var method in type.Methods.Where(m=>m.HasBody))
                {
                    var destination=method.Name=="OpenSettingsNative"||method.Name=="OpenSettings"||method.Name=="PairChosen"||method.Name=="OnGenericParamsUpdated"?report.MenuErrors:errors;
                    foreach(var instruction in method.Body.Instructions)
                    {
                        if(instruction.Operand is MethodReference mr&&IsGame(mr.DeclaringType))
                        {
                            var owner=NativeType(native,mr.DeclaringType);
                            if(owner==null||!PublicType(owner)||!owner.Methods.Any(m=>SameMethod(m,mr)&&Accessible(m.IsPublic,m.IsFamily||m.IsFamilyOrAssembly,type,owner,consumer,native)))destination.Add("Referenced method changed/inaccessible: "+mr.FullName);
                        }
                        else if(instruction.Operand is FieldReference fr&&IsGame(fr.DeclaringType))
                        {
                            var owner=NativeType(native,fr.DeclaringType);
                            bool isStatic=instruction.OpCode.Code==Code.Ldsfld||instruction.OpCode.Code==Code.Ldsflda||instruction.OpCode.Code==Code.Stsfld;
                            if(owner==null||!PublicType(owner)||!owner.Fields.Any(f=>f.Name==fr.Name&&f.IsStatic==isStatic&&SameType(f.FieldType,fr.FieldType)&&Accessible(f.IsPublic,f.IsFamily||f.IsFamilyOrAssembly,type,owner,consumer,native)))destination.Add("Referenced field changed/inaccessible: "+fr.FullName);
                        }
                    }
                    if(method.IsVirtual&&!method.IsNewSlot)
                    {
                        var bases=BaseTypes(type,consumer,native).Where(t=>t.Module==native||NativeAssembly(t.Module.Assembly.Name.Name)).ToArray();
                        if(bases.Length>0)
                        {
                            var target=bases.SelectMany(t=>t.Methods).FirstOrDefault(m=>SameMethod(m,method));
                            if(target==null||!target.IsVirtual||target.IsFinal||!Accessible(target.IsPublic,target.IsFamily||target.IsFamilyOrAssembly,type,target.DeclaringType,consumer,native))
                                destination.Add("Inherited override changed/inaccessible: "+method.FullName);
                        }
                    }
                }
                foreach(var implemented in type.Interfaces.Where(i=>IsGame(i.InterfaceType)))
                    CheckInterface(native,consumer,type,implemented.InterfaceType,errors);
            }
        }
        private static IEnumerable<TypeDefinition> BaseTypes(TypeDefinition type,ModuleDefinition consumer,ModuleDefinition native)
        {
            var next=type.BaseType;var visited=new HashSet<string>();
            while(next!=null&&visited.Add(next.FullName))
            {
                var resolved=IsGame(next)?NativeType(native,next):consumer.GetType(next.GetElementType().FullName);
                if(resolved==null)yield break;
                yield return resolved;next=resolved.BaseType;
            }
        }
        private static bool Accessible(bool isPublic,bool isProtected,TypeDefinition caller,TypeDefinition owner,ModuleDefinition consumer,ModuleDefinition native)
            =>isPublic||(isProtected&&BaseTypes(caller,consumer,native).Any(t=>t.FullName==owner.FullName));
        private static bool PublicType(TypeDefinition type)=>type.IsNested?type.IsNestedPublic&&PublicType(type.DeclaringType):type.IsPublic;
        private static void CheckInterface(ModuleDefinition native,ModuleDefinition consumer,TypeDefinition implementation,TypeReference reference,List<string> errors)
        {
            var contract=NativeType(native,reference);
            if(contract==null||!contract.IsInterface){errors.Add("Required interface changed: "+reference.FullName);return;}
            foreach(var required in contract.Methods.Where(m=>m.IsAbstract))
                if(!implementation.Methods.Any(m=>m.IsPublic&&SameMethod(m,required)))errors.Add("Interface member not implemented: "+required.FullName);
            foreach(var parent in contract.Interfaces)CheckInterface(native,consumer,implementation,parent.InterfaceType,errors);
        }
        private static bool NativeAssembly(string name)=>name=="Assembly-CSharp"||name=="Assembly-CSharp-firstpass"||name=="Mirror"
            ||name.StartsWith("UnityEngine",StringComparison.Ordinal)||name=="Unity.Collections"||name=="Unity.Mathematics";
        private static bool IsGame(TypeReference type)=>NativeAssembly(type.GetElementType().Scope.Name.Replace(".dll",""));
        private static TypeDefinition NativeType(ModuleDefinition native,TypeReference type)
        {
            var element=type.GetElementType();
            if(element.Scope.Name=="Assembly-CSharp"||element.Scope.Name=="Assembly-CSharp.dll"||element.Scope==native)return native.GetType(element.FullName);
            if(element.Scope is AssemblyNameReference name)return native.AssemblyResolver.Resolve(name).MainModule.GetType(element.FullName);
            return null;
        }
        private static bool SameType(TypeReference a,TypeReference b)
        {
            if(a.IsGenericParameter&&b.IsGenericParameter)return ((GenericParameter)a).Position==((GenericParameter)b).Position;
            return a.FullName==b.FullName;
        }
        private static bool SameMethod(MethodDefinition a,MethodReference reference)
        {
            var b=reference is GenericInstanceMethod generic?generic.ElementMethod:reference;
            return a.Name==b.Name&&a.HasThis==b.HasThis&&a.GenericParameters.Count==b.GenericParameters.Count&&SameType(a.ReturnType,b.ReturnType)
                &&a.Parameters.Count==b.Parameters.Count&&a.Parameters.Zip(b.Parameters,(x,y)=>SameType(x.ParameterType,y.ParameterType)).All(x=>x);
        }
    }
}
