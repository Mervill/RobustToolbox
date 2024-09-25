using System;
using System.Collections.Generic;
using System.Linq;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Weavers
{
    public class ModuleWeaver : BaseModuleWeaver
    {
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

            foreach(var typeDef in ModuleDefinition.GetTypes())
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
            var methodFilename = "NoSource";
            var methodLineNumber = 0;

            if (methodSequencePoint != null)
            {
                methodFilename = methodSequencePoint.Document.Url;
                methodLineNumber = methodSequencePoint.StartLine;
            }

            var methodRef = ModuleDefinition.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));

            //var il = methodDef.Body.GetILProcessor();
            var instructionCollection = methodDef.Body.Instructions;

            var ret = Instruction.Create(OpCodes.Ret);
            var leave = Instruction.Create(OpCodes.Leave_S, ret);
            var endFinally = Instruction.Create(OpCodes.Endfinally);
            var writeLine = Instruction.Create(OpCodes.Call, methodRef);
            //var loadString = Instruction.Create(OpCodes.Ldstr, methodFilename);
            var loadString = Instruction.Create(OpCodes.Ldstr, "hello");

            var index = 0;
            while (true)
            {
                if (index >= instructionCollection.Count)
                    break;

                var instr = instructionCollection[index];

                if (instr.OpCode == OpCodes.Ret)
                {
                    instructionCollection.Insert(index, leave);
                    instructionCollection.RemoveAt(index + 1);
                }

                index++;
            }

            instructionCollection.Add(loadString);
            instructionCollection.Add(writeLine);
            instructionCollection.Add(endFinally);
            instructionCollection.Add(ret);

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
