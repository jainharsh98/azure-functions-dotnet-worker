﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Tests.WorkerExtensionsSample;
using Microsoft.Azure.Functions.Worker.Sdk.Generators;
using Microsoft.CodeAnalysis;
using Worker.Extensions.Sample_IncorrectImplementation;
using Xunit;

namespace Microsoft.Azure.Functions.SdkGeneratorTests
{
    public class ExtensionStartupRunnerGeneratorV2Tests
    {
        const string InputCode = @"
public class Foo
{
}";
        [Fact]
        public async Task StartupExecutorCodeGetsGenerated()
        {
            // Source generation is based on referenced assembly.
            var referencedExtensionAssemblies = new[]
            {
                typeof(SampleExtensionStartup).Assembly,
            };
            string expectedOutput = @"// <auto-generated/>
using System;
using Microsoft.Azure.Functions.Worker.Core;
[assembly: WorkerExtensionStartupCodeExecutorInfo(typeof(Microsoft.Azure.Functions.Worker.WorkerExtensionStartupCodeExecutor))]
namespace Microsoft.Azure.Functions.Worker
{
    internal class WorkerExtensionStartupCodeExecutor : WorkerExtensionStartup
    {
        public override void Configure(IFunctionsWorkerApplicationBuilder applicationBuilder)
        {
            try
            {
                new Microsoft.Azure.Functions.Tests.WorkerExtensionsSample.SampleExtensionStartup().Configure(applicationBuilder);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine('Error calling Configure on Microsoft.Azure.Functions.Tests.WorkerExtensionsSample.SampleExtensionStartup instance.'+ex.ToString());
            }
        }
    }
}
".Replace("'", "\"");

            await RunTestAsync(referencedExtensionAssemblies, InputCode, expectedOutput);
        }

        [Fact]
        public async Task StartupExecutorCodeDoesNotGetsGeneratedWheNoExtensionAssembliesAreReferenced()
        {
            // source gen will happen only when an assembly with worker startup type is defined.
            var referencedExtensionAssemblies = Array.Empty<Assembly>();

            string? expectedOutput = null;

            await RunTestAsync(referencedExtensionAssemblies, InputCode, expectedOutput);
        }
        [Fact]
        public async Task DiagnosticErrorsAreReportedWhenStartupTypeIsInvalid()
        {
            var referencedExtensionAssemblies = new[]
            {
                // An assembly with valid extension startup implementation
                typeof(SampleExtensionStartup).Assembly,
                // and an assembly with invalid implementation
                typeof(SampleIncorrectExtensionStartup).Assembly,
            };

            // Our generator will create code for the good implementation
            // and report 2 diagnostic errors for the bad implementation.
            string expectedOutput = @"// <auto-generated/>
using System;
using Microsoft.Azure.Functions.Worker.Core;
[assembly: WorkerExtensionStartupCodeExecutorInfo(typeof(Microsoft.Azure.Functions.Worker.WorkerExtensionStartupCodeExecutor))]
namespace Microsoft.Azure.Functions.Worker
{
    internal class WorkerExtensionStartupCodeExecutor : WorkerExtensionStartup
    {
        public override void Configure(IFunctionsWorkerApplicationBuilder applicationBuilder)
        {
            try
            {
                new Microsoft.Azure.Functions.Tests.WorkerExtensionsSample.SampleExtensionStartup().Configure(applicationBuilder);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine('Error calling Configure on Microsoft.Azure.Functions.Tests.WorkerExtensionsSample.SampleExtensionStartup instance.'+ex.ToString());
            }
        }
    }
}
".Replace("'", "\"");

            var diag1 = (DiagnosticDescriptors.IncorrectBaseType,
                "'Worker.Extensions.Sample_IncorrectImplementation.SampleIncorrectExtensionStartup' must derive from" 
                + " 'Microsoft.Azure.Functions.Worker.Core.WorkerExtensionStartup'.");
            var diag2 = (DiagnosticDescriptors.ConstructorMissing,
                "'Worker.Extensions.Sample_IncorrectImplementation.SampleIncorrectExtensionStartup' class must have a public parameterless constructor.");
            var expectedDiagnosticResults = new List<(DiagnosticDescriptor Descriptor, string Message)>
            { 
                diag2, 
                diag1 
            };

            await RunTestAsync(referencedExtensionAssemblies, InputCode, expectedOutput, expectedDiagnosticResults);
        }

        private async Task RunTestAsync(IEnumerable<Assembly> assemblyReferences, 
            string inputSource,
            string? expectedOutput,
            IReadOnlyList<(DiagnosticDescriptor Descriptor, string Message)> expectedDiagnostics = null)
        {
            var (diagnosticEntries, generatedSourceEntries) = await RoslynTestUtils.RunGenerator(
                new ExtensionStartupRunnerGeneratorV2(),
                assemblyReferences,
                new[] { inputSource }).ConfigureAwait(false);
            
            ValidateDiagnostics(expectedDiagnostics, diagnosticEntries);

            if (expectedOutput is not null)
            {
                Assert.Single(generatedSourceEntries);
            }

            var actualSourceText = generatedSourceEntries.FirstOrDefault().SourceText?.ToString();
            Assert.Equal(expectedOutput, actualSourceText);
        }

        private void ValidateDiagnostics(
            IReadOnlyList<(DiagnosticDescriptor Descriptor, string Message)> expectedDiagnostics,
            ImmutableArray<Diagnostic> actualDiagnostics)
        {
            if (expectedDiagnostics is not null)
            {
                Assert.Equal(expectedDiagnostics.Count, actualDiagnostics.Length);

                for (var i = 0; i < actualDiagnostics.Length; i++)
                {
                    var actual = actualDiagnostics[i];
                    var expected = expectedDiagnostics[i];
                    Assert.Equal(expected.Descriptor, actual.Descriptor);
                    Assert.Equal(expected.Message, actual.GetMessage());
                }
            }
            else
            {
                Assert.Empty(actualDiagnostics);
            }
        }
    }
}
