using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

using CKAN.Extensions;

namespace Tests
{
    [TestFixture]
    public class ProcessStartInfoTests : MonoCecilAnalysisBase
    {
        [TestCase(new object[]
                  {
                      typeof(CKAN.NullUser),
                      typeof(CKAN.CmdLine.MainClass),
                      typeof(CKAN.ConsoleUI.ConsoleCKAN),
                      typeof(CKAN.NetKAN.Program),
                      #if NETFRAMEWORK || WINDOWS
                      typeof(CKAN.AutoUpdateHelper.Program),
                      typeof(CKAN.GUI.Main),
                      #endif
                  })]
        public void Assemblymodule_ProcessStartInfo_UseShellExecuteAlwaysSetExplicitly(object[] anchorTypes)
        {
            // Arrange
            var methodsByName = AllMethodsByFullyQualifiedName(anchorTypes.OfType<Type>()
                                                                          .Select(Assembly.GetAssembly)
                                                                          .OfType<Assembly>());

            // Act / Assert
            Assert.Multiple(() =>
            {
                foreach ((string name, MethodDefinition meth) in methodsByName)
                {
                    foreach (var newObj in meth.Body
                                               .Instructions
                                               .Where(i => i.OpCode == OpCodes.Newobj
                                                           && i.Operand is MethodReference
                                                           {
                                                               DeclaringType: { FullName: "System.Diagnostics.ProcessStartInfo" }
                                                           }))
                    {
                        Assert.IsTrue(PropertySets(newObj).Any(s => s.Operand is MethodReference { Name: "set_UseShellExecute" }),
                                      $"{name} creates ProcessStartInfo without setting UseShellExecute, which defaults to true in .NET Framework and false in .NET Core");
                    }
                }
            });
        }

        private static IEnumerable<Instruction> PropertySets(Instruction newObj)
            => newObj.Operand is MethodReference { DeclaringType: { FullName: var typeName } }
                   ? newObj.TraverseNodes(i => i.Next)
                           .Where(i => i.OpCode == OpCodes.Callvirt
                                       && i.Operand is MethodReference methRef
                                       && methRef.DeclaringType.FullName == typeName
                                       && methRef.Name.StartsWith("set_"))
                   : Enumerable.Empty<Instruction>();
    }
}
