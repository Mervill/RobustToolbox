using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Weavers
{
    public class ModuleWeaver : BaseModuleWeaver
    {
        const string TracyProfilerFullName = "Robust.Tracy.TracyProfiler";
        const string TracyProfilerZoneFullName = "Robust.Tracy.TracyZone";

        const string ZoneOptionsAttributeName = "TracyAutowireZoneOptionsAttribute";

        int AssemblyDefaultColor = 0;

        MethodReference IDisposableDisposeRef;

        MethodReference BeginZoneMethodRef;

        TypeReference TracyZoneTypeRef;

        readonly List<string> ClassAttributeIgnoreNames = new List<string>()
        {
            nameof(CompilerGeneratedAttribute),
            "TracyAutowireIgnoreAttribute",
        };

        readonly List<string> MethodAttributeIgnoreNames = new List<string>()
        {
            //nameof(DebuggerHiddenAttribute),
            nameof(IteratorStateMachineAttribute), // No source location
            nameof(AsyncStateMachineAttribute), // No source location
            nameof(CompilerGeneratedAttribute),
            "TracyAutowireIgnoreAttribute",
        };

        public override void Execute()
        {
            //System.Diagnostics.Debugger.Launch();

            /*
            var type = GetType();
            var typeDefinition = new TypeDefinition(
                @namespace: type.Assembly.GetName().Name,
                name: $"TypeInjectedBy{type.Name}",
                attributes: TypeAttributes.Public,
                baseType: TypeSystem.ObjectReference);
            ModuleDefinition.Types.Add(typeDefinition);
            */

            // Get references

            IDisposableDisposeRef = ModuleDefinition.ImportReference(typeof(IDisposable).GetMethod("Dispose"));

            IAssemblyResolver currentAssemblyResolver = ModuleDefinition.AssemblyResolver;
            AssemblyNameReference robustTracyNameReference = ModuleDefinition.AssemblyReferences.FirstOrDefault(x => x.Name == "Robust.Tracy");

            /*foreach (var assem in ModuleDefinition.AssemblyReferences)
            {
                WriteWarning($"---- {assem.Name}");
            }*/

            if (robustTracyNameReference == null)
            {
                WriteError($"ModuleDefinition does not contain a reference to `Robust.Tracy`!");
                return;
            }

            AssemblyDefinition robustTracyAssemblyReference = currentAssemblyResolver.Resolve(robustTracyNameReference);
            ModuleDefinition robustTracyMainModule = robustTracyAssemblyReference.MainModule;

            TypeDefinition profilerType = robustTracyMainModule.GetType(TracyProfilerFullName);
            MethodDefinition beginZone = profilerType.Methods.First(x => x.Name == "BeginZone");            
            BeginZoneMethodRef = ModuleDefinition.ImportReference(beginZone);

            TypeDefinition profilerZoneType = robustTracyMainModule.GetType(TracyProfilerZoneFullName);
            TracyZoneTypeRef = ModuleDefinition.ImportReference(profilerZoneType);

            var assemblyOptions = ModuleDefinition.Assembly.CustomAttributes.FirstOrDefault(x => x.AttributeType.Name == "TracyAutowireAssemblyDefaultsAttribute");
            if (assemblyOptions != null)
            {
                /*WriteWarning("assemblyOptions:");
                WriteWarning($"    {assemblyOptions.HasConstructorArguments}");
                WriteWarning($"    {assemblyOptions.HasFields}");
                WriteWarning($"    {assemblyOptions.HasProperties}");*/

                AssemblyDefaultColor = (int)((uint)assemblyOptions.ConstructorArguments[0].Value);
                WriteWarning($"AssemblyDefaultColor: {AssemblyDefaultColor}");
            }

            // Execute the Weaver

            //ExternalTracyWeaver(); // not working!

            InternalTracyWeaver();
        }

        // internal weave, try to wrap each instructions functions in profiler hooks
        private void InternalTracyWeaver()
        {
            foreach (var typeDef in ModuleDefinition.GetTypes())
            {
                // filters out interfaces
                if (!typeDef.IsClass)
                    continue;

                if (typeDef.Name.StartsWith("Tracy"))
                    continue;

                if (typeDef.CustomAttributes.Any(x => ClassAttributeIgnoreNames.Contains(x.AttributeType.Name)))
                    continue;

                foreach (var methodDef in typeDef.Methods)
                {
                    if (methodDef.IsAbstract)
                        continue;

                    if (methodDef.AggressiveInlining)
                        continue;

                    if (methodDef.IsSetter || methodDef.IsGetter)
                        continue;

                    // optional - operators and constructors
                    if (methodDef.IsSpecialName)
                        continue;

                    if (methodDef.CustomAttributes.Any(x => MethodAttributeIgnoreNames.Contains(x.AttributeType.Name)))
                        continue;

                    AddTracyZone(typeDef, methodDef);
                }
            }
        }

        private void AddTracyZone(TypeDefinition parentTypeDef, MethodDefinition methodDef)
        {
            // This check needs to happen here or else GetSequencePoint() will throw because
            // it just accesses methodDef.Body without checking for null.
            if (methodDef.Body == null)
            {
                WriteDebug($"Rejecting {methodDef.FullName} because it has a null method body.");
                return;
            }

            var methodLineNumber = 0;
            var methodFilename = "NoSource";
            var methodSequencePoint = methodDef.GetSequencePoint();
            if (methodSequencePoint != null)
            {
                methodLineNumber = methodSequencePoint.StartLine;
                methodFilename = methodSequencePoint.Document.Url;
            }
            else
            {
                WriteWarning($"Rejecting {methodDef.FullName} because it has no sequence point (likely compiler generated method).");
                return;
            }

            var methodName = methodDef.FullName;

            /*{
                var methodSplit = methodName.Split(' ');
                var returnName = methodSplit[0];
                var fullInvoke = methodSplit[1];

                var invokeSplit = fullInvoke.Split(new string[] { "::" }, StringSplitOptions.None);
                var namespacePath = invokeSplit[0];
                var nameArgs = invokeSplit[1];

                var argsOpenIndex = nameArgs.IndexOf('(');

                var name = nameArgs.Substring(0, argsOpenIndex);

                methodName = $"{namespacePath}::{name}()";
            }*/

            {
                var methodSplit = methodName.Split(' ');
                //var returnName = methodSplit[0];
                methodName = methodSplit[1];
            }

            int zoneColor = AssemblyDefaultColor;

            var zoneOptions = methodDef.CustomAttributes.FirstOrDefault(x => x.AttributeType.Name == ZoneOptionsAttributeName);
            if (zoneOptions != null)
            {
                //WriteWarning("zoneOptions:");
                //WriteWarning($"    {zoneOptions.HasConstructorArguments}");
                //WriteWarning($"    {zoneOptions.HasFields}");
                //WriteWarning($"    {zoneOptions.HasProperties}");

                /*foreach (var arg in zoneOptions.ConstructorArguments)
                {
                    WriteWarning($"        {arg.Value} ({arg.Value.GetType()})");
                }*/

                zoneColor = (int)((uint)zoneOptions.ConstructorArguments[0].Value);
            }

            var methodBody = methodDef.Body;
            var instructions = methodBody.Instructions;

            var originalLastIndex = instructions.Count - 1;
            var originalReturnInstruction = instructions[originalLastIndex];
            if (originalReturnInstruction.OpCode != OpCodes.Ret)
            {
                if (originalReturnInstruction.OpCode == OpCodes.Throw)
                {
                    WriteDebug($"Rejecting {methodDef.FullName} because it ends with a throw instruction (likely unimplemented method).");
                }
                else
                {
                    WriteWarning($"Rejecting {methodDef.FullName} because it does not end with a RET or THROW instruction: `{originalReturnInstruction.OpCode}`.");
                }
                return;
            }

            // we need a variable to hold the TracyZone
            var vardefTracyZone = new VariableDefinition(TracyZoneTypeRef);
            methodBody.Variables.Add(vardefTracyZone);

            // == prologue

            Instruction[] prologue = new[]
            {
                // zoneName
                Instruction.Create(OpCodes.Ldnull),
                // active
                Instruction.Create(OpCodes.Ldc_I4_1),
                // color
                Instruction.Create(OpCodes.Ldc_I4, zoneColor),
                // text
                Instruction.Create(OpCodes.Ldnull),
                // lineNumber
                Instruction.Create(OpCodes.Ldc_I4, methodLineNumber),
                // filePath
                Instruction.Create(OpCodes.Ldstr, methodFilename),
                // memberName
                Instruction.Create(OpCodes.Ldstr, methodName),
                // call Robust.Shared.Profiling.TracyProfiler.BeginZone
                Instruction.Create(OpCodes.Call, BeginZoneMethodRef),
                // store
                Instruction.Create(OpCodes.Stloc, vardefTracyZone),
            };

            for (int x = prologue.Length - 1; x >= 0; x--)
            {
                instructions.Insert(0, prologue[x]);
            }

            // == epilogue

            Instruction[] epilogue = new[]
            {
                // This emits: ((IDisposable)tracyZone).Dispose();
                Instruction.Create(OpCodes.Ldloca, vardefTracyZone),
                Instruction.Create(OpCodes.Constrained, TracyZoneTypeRef),
                Instruction.Create(OpCodes.Callvirt, IDisposableDisposeRef),
            };

            var epilogueStart = epilogue[0];

            for (int x = 0; x < epilogue.Length; x++)
            {
                instructions.Insert(instructions.Count - 1, epilogue[x]);
            }

            // any instruction that was targeting `ret` now needs to target the start of the epilogue
            for (int idx = 0; idx < instructions.Count; idx++)
            {
                var instr = instructions[idx];
                if (instr.Operand != null && instr.Operand == originalReturnInstruction)
                {
                    instr.Operand = epilogueStart;
                }
            }

            // any handler that ended on the `ret` now needs to end on the start of the epilogue
            foreach (var handler in methodBody.ExceptionHandlers)
            {
                if (handler.HandlerEnd == originalReturnInstruction)
                {
                    handler.HandlerEnd = epilogueStart;
                }
            }

            /*var index = 0;
            while (true)
            {
                if (index >= instructions.Count)
                    break;

                var instr = instructions[index];

                if (instr.OpCode == OpCodes.Ret)
                {
                    instructions.Insert(index, leave);
                    instructions.RemoveAt(index + 1);
                }

                index++;
            }*/

            /*var handler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = originalBodyStart,
                TryEnd = loadString,
                HandlerStart = loadString,
                HandlerEnd = ret,
            };*/

            //methodBody.ExceptionHandlers.Add(handler);

            methodBody.Optimize();
        }

        private void GetModuleZoneOptions(MethodDefinition methodDef)
        {
            
        }

        // external weave: try to find every CALL instruction and wrap it in profiler hooks
        private void ExternalTracyWeaver()
        {
            var consoleWriteLineRef = ModuleDefinition.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));

            foreach (var typeDef in ModuleDefinition.GetTypes())
            {
                if (!typeDef.IsClass)
                    continue;

                if (typeDef.Name.StartsWith("Tracy"))
                    continue;

                foreach (var methodDef in typeDef.Methods)
                {
                    //if (!methodDef.IsManaged)
                    //    continue;

                    if (methodDef.IsAbstract)
                        continue;

                    if (methodDef.AggressiveInlining)
                        continue;

                    if (methodDef.IsSetter || methodDef.IsGetter)
                        continue;

                    var methodBody = methodDef.Body;
                    if (methodBody == null)
                        continue;

                    var methodLineNumber = 0;
                    var methodFilename = "NoSource";
                    var methodSequencePoint = methodDef.GetSequencePoint();
                    if (methodSequencePoint != null)
                    {
                        methodLineNumber = methodSequencePoint.StartLine;
                        methodFilename = methodSequencePoint.Document.Url;
                    }

                    var instructions = methodBody.Instructions;

                    var index = 0;
                    while (true)
                    {
                        if (index >= instructions.Count)
                            break;

                        var instr = instructions[index];

                        if (instr.OpCode == OpCodes.Call)
                        {
                            var callsiteMethod = instr.Operand as MethodReference;
                            //callsiteMethod.
                            //var callsiteSequencePoint = callsiteMethod

                            instructions.Insert(index,
                                Instruction.Create(OpCodes.Call, consoleWriteLineRef));
                        
                            instructions.Insert(index,
                                Instruction.Create(OpCodes.Ldstr, $"Zone Begin {callsiteMethod.FullName}"));

                            index += 2;

                            /*instructions.Insert(index + 1,
                                Instruction.Create(OpCodes.Ldstr, "Zone End"));

                            instructions.Insert(index + 2,
                                Instruction.Create(OpCodes.Call, consoleWriteLineRef));

                            index += 2;*/
                        }
                        index++;
                    }
                }
            }
        }

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            return Enumerable.Empty<string>();
        }
    }
}
