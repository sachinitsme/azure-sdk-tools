﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------


namespace Microsoft.WindowsAzure.Management.ServiceManagement.HostedServices
{
    using System;
    using System.Net;
    using System.Management.Automation;
    using System.ServiceModel;
    using Utilities.Common;
    using WindowsAzure.ServiceManagement;
    using Extensions;
    using Helpers;
    using Properties;

    /// <summary>
    /// Create a new deployment. Note that there shouldn't be a deployment 
    /// of the same name or in the same slot when executing this command.
    /// </summary>
    [Cmdlet(VerbsCommon.New, "AzureDeployment", DefaultParameterSetName = "PaaS"), OutputType(typeof(ManagementOperationContext))]
    public class NewAzureDeploymentCommand : ServiceManagementBaseCmdlet
    {
        public NewAzureDeploymentCommand()
        {
        }

        public NewAzureDeploymentCommand(IServiceManagement channel)
        {
            Channel = channel;
        }

        [Parameter(Position = 0, Mandatory = true, ValueFromPipelineByPropertyName = true, HelpMessage = "Cloud service name.")]
        [ValidateNotNullOrEmpty]
        public string ServiceName
        {
            get;
            set;
        }

        [Parameter(Position = 1, Mandatory = true, HelpMessage = "Package location. This parameter specifies the path or URI to a .cspkg in blob storage. The storage account must belong to the same subscription as the deployment.")]
        [ValidateNotNullOrEmpty]
        public string Package
        {
            get;
            set;
        }

        [Parameter(Position = 2, Mandatory = true,  HelpMessage = "Configuration file path. This parameter should specifiy a .cscfg file on disk.")]
        [ValidateNotNullOrEmpty]
        public string Configuration
        {
            get;
            set;
        }

        [Parameter(Position = 3, Mandatory = true, ValueFromPipelineByPropertyName = true, HelpMessage = "Deployment slot [Staging | Production].")]
        [ValidateSet(DeploymentSlotType.Staging, DeploymentSlotType.Production, IgnoreCase = true)]
        public string Slot
        {
            get;
            set;
        }

        [Parameter(Position = 4, Mandatory = false, HelpMessage = "Label for the new deployment.")]
        [ValidateNotNullOrEmpty]
        public string Label
        {
            get;
            set;
        }

        [Parameter(Position = 5, HelpMessage = "Deployment name.")]
        [Alias("DeploymentName")]
        [ValidateNotNullOrEmpty]
        public string Name
        {
            get;
            set;
        }
        
        [Parameter(Mandatory = false, HelpMessage = "Do not start deployment upon creation.")]
        public SwitchParameter DoNotStart
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, HelpMessage = "Indicates whether to treat package validation warnings as errors.")]
        public SwitchParameter TreatWarningsAsError
        {
            get;
            set;
        }

        [Parameter(Mandatory = false, HelpMessage = "Extension configurations.")]
        public ExtensionConfigurationContext[] ExtensionConfiguration
        {
            get;
            set;
        }

        public void NewPaaSDeploymentProcess()
        {
            bool removePackage = false;

            AssertNoPersistenVmRoleExistsInDeployment(DeploymentSlotType.Production);
            AssertNoPersistenVmRoleExistsInDeployment(DeploymentSlotType.Staging);

            var storageName = CurrentSubscription.CurrentStorageAccount;

            Uri packageUrl;
            if (this.Package.StartsWith(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                this.Package.StartsWith(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                packageUrl = new Uri(this.Package);
            }
            else
            {
                var progress = new ProgressRecord(0, Resources.WaitForUploadingPackage, Resources.UploadingPackage);
                WriteProgress(progress);
                removePackage = true;
                packageUrl = this.RetryCall(s =>
                    AzureBlob.UploadPackageToBlob(
                    this.Channel,
                    storageName,
                    s,
                    this.Package,
                    null));
            }

            ExtensionConfiguration extConfig = null;
            if (ExtensionConfiguration != null)
            {
                ExtensionManager extensionMgr = new ExtensionManager(Channel, CurrentSubscription.SubscriptionId, ServiceName);
                ExtensionConfigurationBuilder configBuilder = extensionMgr.GetBuilder();
                foreach (ExtensionConfigurationContext context in ExtensionConfiguration)
                {
                    if (context.X509Certificate != null)
                    {
                        var operationDescription = string.Format("{0} - Uploading Certificate: {1}", CommandRuntime, context.X509Certificate.Thumbprint);
                        ExecuteClientActionInOCS(null, operationDescription, s => this.Channel.AddCertificates(s, this.ServiceName, CertUtils.Create(context.X509Certificate)));
                    }

                    ExtensionConfiguration outConfig = null;
                    extensionMgr.InstallExtension(context, Slot, ref outConfig);
                    configBuilder.Add(outConfig);
                }
                extConfig = configBuilder.ToConfiguration();
            }
            
            var deploymentInput = new CreateDeploymentInput
            {
                PackageUrl = packageUrl,
                Configuration = General.GetConfiguration(this.Configuration),
                ExtensionConfiguration = extConfig,
                Label = this.Label,
                Name = this.Name,
                StartDeployment = !this.DoNotStart.IsPresent,
                TreatWarningsAsError = this.TreatWarningsAsError.IsPresent
            };
            
            using (new OperationContextScope(Channel.ToContextChannel()))
            {
                try
                {
                    var progress = new ProgressRecord(0, Resources.WaitForUploadingPackage, Resources.CreatingNewDeployment);
                    WriteProgress(progress);

                    ExecuteClientAction(deploymentInput, CommandRuntime.ToString(), s => this.Channel.CreateOrUpdateDeployment(s, this.ServiceName, this.Slot, deploymentInput));

                    if (removePackage == true)
                    {
                        this.RetryCall(s =>
                        AzureBlob.DeletePackageFromBlob(
                                this.Channel,
                                storageName,
                                s,
                                packageUrl));
                    }
                }
                catch (ServiceManagementClientException ex)
                {
                    this.WriteErrorDetails(ex);
                }
            }
        }

        private void AssertNoPersistenVmRoleExistsInDeployment(string slot)
        {
            using (new OperationContextScope(Channel.ToContextChannel()))
            {
                try
                {
                    Deployment currentDeployment = this.RetryCall(s => this.Channel.GetDeploymentBySlot(s, this.ServiceName, slot));
                    if (currentDeployment.RoleList != null)
                    {
                        if (string.Compare(currentDeployment.RoleList[0].RoleType, "PersistentVMRole", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            throw new ArgumentException(String.Format(Resources.CanNotCreateNewDeploymentWhileVMsArePresent, slot));
                        }
                    }
                }
                catch (ServiceManagementClientException ex)
                {
                    if (ex.HttpStatus != HttpStatusCode.NotFound && IsVerbose() == false)
                    {
                        this.WriteErrorDetails(ex);
                    }
                }
            }
        }

        protected override void OnProcessRecord()
        {
            this.ValidateParameters();
            this.NewPaaSDeploymentProcess();
        }

        private void ValidateParameters()
        {
            if (string.IsNullOrEmpty(this.Slot))
            {
                this.Slot = DeploymentSlotType.Production;
            }

            if (string.IsNullOrEmpty(this.Name))
            {
                 this.Name = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrEmpty(this.Label))
            {
                this.Label = this.Name;
            }

            if (string.IsNullOrEmpty(this.CurrentSubscription.CurrentStorageAccount))
            {
                throw new ArgumentException(Resources.CurrentStorageAccountIsNotSet);                
            }
        }
    }
}
