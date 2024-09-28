using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Weavers
{
    public class ModuleWeaver : BaseModuleWeaver
    {
        const string TracyProfilerFullName = "Robust.Shared.Profiling.TracyProfiler";
        const string TracyProfilerZoneFullName = "Robust.Shared.Profiling.TracyZone";
        const string TracyProfilerBeginZoneFullName = "Robust.Shared.Profiling.TracyZone Robust.Shared.Profiling.TracyProfiler::BeginZone(System.String,System.Boolean,System.UInt32,System.String,System.UInt32,System.String,System.String)";

        MethodReference IDisposableDisposeRef;

        TypeDefinition TracyZoneTypeDef;
        MethodDefinition BeginZoneMethodDef;

        public override void Execute()
        {
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

            //ExternalTracyWeaver();

            //InternalTracyWeaver();
        }

        // internal weave, try to wrap each instructions functions in profiler hooks
        private void InternalTracyWeaver()
        {
            foreach (var typeDef in ModuleDefinition.GetTypes())
            {
                if (!typeDef.IsClass)
                    continue;

                if (typeDef.Name.StartsWith("Tracy"))
                    continue;

                foreach (var methodDef in typeDef.Methods)
                {
                    if (methodDef.IsAbstract)
                        continue;

                    if (methodDef.AggressiveInlining)
                        continue;

                    if (methodDef.IsSetter || methodDef.IsGetter)
                        continue;

                    AddTracyZone(typeDef, methodDef);
                }
            }
        }

        private void AddTracyZone(TypeDefinition parentTypeDef, MethodDefinition methodDef)
        {
            if (methodDef.Body == null)
                return;

            if (methodDef.ReturnType.FullName != "System.Void")
                return;

            var methodLineNumber = 0;
            var methodFilename = "NoSource";
            var methodSequencePoint = methodDef.GetSequencePoint();
            if (methodSequencePoint != null)
            {
                methodLineNumber = methodSequencePoint.StartLine;
                methodFilename = methodSequencePoint.Document.Url;
            }

            var consoleWriteLineRef = ModuleDefinition.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));

            // == prologue

            var vardef = new VariableDefinition(TracyZoneTypeDef);

            Instruction[] prologue = new[]
            {
                /*
                Instruction.Create(OpCodes.Ldstr, "Zone Begin"),
                Instruction.Create(OpCodes.Call, consoleWriteLineRef),
                */

                // zone name
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
                Instruction.Create(OpCodes.Ldstr, methodDef.FullName),
                // call Robust.Shared.Profiling.TracyProfiler.BeginZone
                Instruction.Create(OpCodes.Call, BeginZoneMethodDef),
                // store
                Instruction.Create(OpCodes.Stloc, vardef)
            };

            // something wrong with the call not removing the stack correctly?

            // == epilogue

            var ret = Instruction.Create(OpCodes.Ret);
            var leave = Instruction.Create(OpCodes.Leave, ret);
            var endFinally = Instruction.Create(OpCodes.Endfinally);
            var writeLine = Instruction.Create(OpCodes.Call, consoleWriteLineRef);
            var loadString = Instruction.Create(OpCodes.Ldstr, "Zone End");
            //var loadString = Instruction.Create(OpCodes.Ldstr, methodFilename);
            
            Instruction[] epilogue = new[]
            {
                loadString,
                writeLine,
                endFinally,
                ret,
            };

            // ==

            var methodBody = methodDef.Body;
            var instructions = methodBody.Instructions;

            methodBody.Variables.Add(vardef);

            var originalBodyStart = instructions.First();

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

            for (int x = prologue.Length - 1; x >= 0; x--)
            {
                instructions.Insert(0, prologue[x]);
            }

            var beforeRet = instructions.Count - 1;

            instructions.Insert(beforeRet,
                Instruction.Create(OpCodes.Callvirt, IDisposableDisposeRef));

            instructions.Insert(beforeRet,
                Instruction.Create(OpCodes.Constrained, TracyZoneTypeDef));

            instructions.Insert(beforeRet,
                Instruction.Create(OpCodes.Ldloca, vardef));

            /*for (int y = 0; y < epilogue.Length; y++)
            {
                instructions.Add(epilogue[y]);
            }*/

            /*var handler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = originalBodyStart,
                TryEnd = loadString,
                HandlerStart = loadString,
                HandlerEnd = ret,
            };*/

            //methodBody.ExceptionHandlers.Add(handler);
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
