using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

using TracyProfiler;

namespace Weavers
{
    public class ModuleWeaver : BaseModuleWeaver
    {
        const string TracyProfilerFullName = "Robust.Shared.Profiling.TracyProfiler";
        const string TracyProfilerZoneFullName = "Robust.Shared.Profiling.TracyZone";
        const string TracyProfilerBeginZoneFullName = "Robust.Shared.Profiling.TracyZone Robust.Shared.Profiling.TracyProfiler::BeginZone(System.String,System.Boolean,System.UInt32,System.String,System.UInt32,System.String,System.String)";

        MethodReference IDisposableDisposeRef;

        MethodDefinition BeginZoneMethodDef;

        TypeDefinition TracyZoneTypeDef;

        readonly List<string> ClassAttributeIgnoreNames = new List<string>()
        {
            nameof(CompilerGeneratedAttribute),
            nameof(TracyAutowireIgnoreClassAttribute),
        };

        readonly List<string> MethodAttributeIgnoreNames = new List<string>()
        {
            //nameof(DebuggerHiddenAttribute),
            //nameof(IteratorStateMachineAttribute), // No source location
            //nameof(AsyncStateMachineAttribute), // No source location
            nameof(CompilerGeneratedAttribute),
            nameof(TracyAutowireIgnoreMethodAttribute),
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

            IDisposableDisposeRef = ModuleDefinition.ImportReference(typeof(IDisposable).GetMethod("Dispose"));

            var profilerType = ModuleDefinition.GetType(TracyProfilerFullName);
            var beginZone = profilerType.Methods.First(x => x.Name == "BeginZone");
            BeginZoneMethodDef = beginZone;

            TracyZoneTypeDef = ModuleDefinition.GetType(TracyProfilerZoneFullName);

            //ExternalTracyWeaver(); // not working!

            InternalTracyWeaver();
        }

        // internal weave, try to wrap each instructions functions in profiler hooks
        private void InternalTracyWeaver()
        {
            foreach (var typeDef in ModuleDefinition.GetTypes())
            {
                if (!typeDef.IsClass)
                    continue;

                //if (typeDef.IsNested)
                //    continue;

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
            if (methodDef.Body == null)
                return;

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
                WriteWarning($"Rejecting {methodDef.FullName} because it has no sequence point!");
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

            var methodBody = methodDef.Body;
            var instructions = methodBody.Instructions;

            var originalLastIndex = instructions.Count - 1;
            var originalReturnInstruction = instructions[originalLastIndex];
            //Debug.Assert(originalReturnInstruction.OpCode == OpCodes.Ret);

            // we need a variable to hold the TracyZone
            var vardefTracyZone = new VariableDefinition(TracyZoneTypeDef);
            methodBody.Variables.Add(vardefTracyZone);

            // == prologue

            Instruction[] prologue = new[]
            {
                // zoneName
                Instruction.Create(OpCodes.Ldnull),
                // active
                Instruction.Create(OpCodes.Ldc_I4_1),
                // color
                Instruction.Create(OpCodes.Ldc_I4_0),
                // text
                Instruction.Create(OpCodes.Ldnull),
                // lineNumber
                Instruction.Create(OpCodes.Ldc_I4, methodLineNumber),
                // filePath
                Instruction.Create(OpCodes.Ldstr, methodFilename),
                // memberName
                Instruction.Create(OpCodes.Ldstr, methodName),
                // call Robust.Shared.Profiling.TracyProfiler.BeginZone
                Instruction.Create(OpCodes.Call, BeginZoneMethodDef),
                // store
                Instruction.Create(OpCodes.Stloc, vardefTracyZone),
            };

            for (int x = prologue.Length - 1; x >= 0; x--)
            {
                instructions.Insert(0, prologue[x]);
            }

            // == epilogue

            // ((IDisposable)tracyZone).Dispose();
            Instruction[] epilogue = new[]
            {
                Instruction.Create(OpCodes.Ldloca, vardefTracyZone),
                Instruction.Create(OpCodes.Constrained, TracyZoneTypeDef),
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
