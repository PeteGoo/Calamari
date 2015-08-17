﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Calamari.Deployment;
using Calamari.Integration.Azure;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Processes;
using Calamari.Integration.Scripting;
using Calamari.Tests.Fixtures.Deployment.Azure;
using NSubstitute;
using NUnit.Framework;
using Octostache;

namespace Calamari.Tests.Fixtures.Azure
{
    [TestFixture]
    public class AzurePowerShellContextFixture
    {
        [Test]
        public void CertificateRemovedAfterScriptExecution()
        {
            OctopusTestAzureSubscription.IgnoreIfCertificateNotInstalled();
            var powershellContext = new AzurePowerShellContext();
            var scriptEngine = Substitute.For<IScriptEngine>();
            var commandLineRunner = Substitute.For<ICommandLineRunner>();

            var variables = new VariableDictionary();
            OctopusTestAzureSubscription.PopulateVariables(variables);

            using (var variablesFile = new TemporaryFile(Path.GetTempFileName()))
            {
                var expectedCertFile = Path.Combine(variablesFile.DirectoryPath, "azure_certificate.pfx");

                scriptEngine
                    .When(engine => engine.Execute(Arg.Any<string>(), variables, commandLineRunner))
                    .Do(callInfo => Assert.True(File.Exists(expectedCertFile)));

                powershellContext.ExecuteScript(scriptEngine, variablesFile.FilePath, variables, commandLineRunner);

                Assert.False(File.Exists(expectedCertFile));
            }
        }
    }
}