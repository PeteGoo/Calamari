﻿using Calamari.Deployment;
using Calamari.Integration.Processes;
using Calamari.Integration.Scripting;
using Calamari.Integration.Scripting.WindowsPowerShell;
using Octostache;

namespace Calamari.Azure.Integration
{
    public class AzurePowerShellScriptEngine : IScriptEngine
    {
        public string[] GetSupportedExtensions()
        {
            return new[] { ScriptType.Powershell.FileExtension() };
        }

        public CommandResult Execute(string scriptFile, VariableDictionary variables, ICommandLineRunner commandLineRunner)
        {
            var powerShellEngine = new PowerShellScriptEngine();
            if (variables.Get(SpecialVariables.Account.AccountType) == "AzureSubscription")
            {
                new AzurePowerShellContext().ExecuteScript(powerShellEngine, scriptFile, variables, commandLineRunner);
            }

            return powerShellEngine.Execute(scriptFile, variables, commandLineRunner);
        }
    }
}