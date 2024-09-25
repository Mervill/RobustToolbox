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
        const string TracyProfilerBeginZoneFullName = "Robust.Shared.Profiling.TracyZone Robust.Shared.Profiling.TracyProfiler::BeginZone(System.String,System.Boolean,System.UInt32,System.String,System.UInt32,System.String,System.String)";

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

            var profilerType = ModuleDefinition.GetAllTypes().First(x => x.Name == "TracyProfiler");
            var beginZone = profilerType.Methods.First(x => x.Name == "BeginZone");
            BeginZoneMethodDef = beginZone;

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

            var methodSequencePoint = methodDef.GetSequencePoint();
            var methodLineNumber = 0;
            var methodFilename = "NoSource";
            
            if (methodSequencePoint != null)
            {
                methodLineNumber = methodSequencePoint.StartLine;
                methodFilename = methodSequencePoint.Document.Url;
            }

            var methodRef = ModuleDefinition.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));

            /*
            ModuleDefinition.TryGetTypeReference("Robust.Shared.Profiling.TracyProfiler", out var tracyProfilerRef);
            var beginZoneMethodDef = tracyProfilerRef.Resolve()
                .GetMethods()
                .First(x => x.Name == "BeginZone");

            var beginZoneMethodRef = ModuleDefinition.ImportReference(beginZoneMethodDef);

            var methodBody = methodDef.Body;
            var instructions = methodBody.Instructions;
            */

            // prologue
            
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
            };
            
            var methodBody = methodDef.Body;
            var instructions = methodBody.Instructions;

            var ret = Instruction.Create(OpCodes.Ret);
            var leave = Instruction.Create(OpCodes.Leave_S, ret);
            var endFinally = Instruction.Create(OpCodes.Endfinally);
            var writeLine = Instruction.Create(OpCodes.Call, methodRef);
            //var loadString = Instruction.Create(OpCodes.Ldstr, methodFilename);
            var loadString = Instruction.Create(OpCodes.Ldstr, BeginZoneMethodDef.FullName);

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

            instructions.Add(loadString);
            instructions.Add(writeLine);
            instructions.Add(endFinally);
            instructions.Add(ret);

            //il.InsertAfter(methodDef.Body.Instructions.Last(), loadString);

            //il.InsertAfter(loadString, writeLine);
            //il.InsertAfter(writeLine, endFinally);
            //il.InsertAfter(endFinally, ret);

            var handler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = methodDef.Body.Instructions.First(),
                TryEnd = loadString,
                HandlerStart = loadString,
                HandlerEnd = ret,
            };

            methodDef.Body.ExceptionHandlers.Add(handler);
        }

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            return Enumerable.Empty<string>();
        }
    }
}
