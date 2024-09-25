using System;
using System.Collections.Generic;
using System.Linq;
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

            var profilerType = ModuleDefinition.GetType(TracyProfilerFullName);
            var beginZone = profilerType.Methods.First(x => x.Name == "BeginZone");
            BeginZoneMethodDef = beginZone;

            TracyZoneTypeDef = ModuleDefinition.GetType(TracyProfilerZoneFullName);

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

            var methodRef = ModuleDefinition.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));

            // == prologue

            var vardef = new VariableDefinition(TracyZoneTypeDef);

            Instruction[] prologue = new[]
            {
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
                Instruction.Create(OpCodes.Stloc, vardef),
            };

            // something wrong with the call not removing the stack correctly?

            // == epilogue

            var ret = Instruction.Create(OpCodes.Ret);
            var leave = Instruction.Create(OpCodes.Leave, ret);
            var endFinally = Instruction.Create(OpCodes.Endfinally);
            var writeLine = Instruction.Create(OpCodes.Call, methodRef);
            var loadString = Instruction.Create(OpCodes.Ldstr, methodFilename);
            //var loadString = Instruction.Create(OpCodes.Ldstr, BeginZoneMethodDef.FullName);

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

            //methodBody.Variables.Add(vardef);

            var originalBodyStart = instructions.First();

            var index = 0;
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
            }

            /*for (int x = prologue.Length - 1; x >= 0; x--)
            {
                instructions.Insert(0, prologue[x]);
            }*/

            for (int y = 0; y < epilogue.Length; y++)
            {
                instructions.Add(epilogue[y]);
            }

            var handler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = originalBodyStart,
                TryEnd = loadString,
                HandlerStart = loadString,
                HandlerEnd = ret,
            };

            methodBody.ExceptionHandlers.Add(handler);
        }

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            return Enumerable.Empty<string>();
        }
    }
}
