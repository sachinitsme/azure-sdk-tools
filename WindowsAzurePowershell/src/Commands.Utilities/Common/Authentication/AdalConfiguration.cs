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

namespace Microsoft.WindowsAzure.Commands.Utilities.Common.Authentication
{
    using System;
    using System.Collections.Generic;
    using IdentityModel.Clients.ActiveDirectory;

    /// <summary>
    /// Class storing the configuration information needed
    /// for ADAL to request token from the right AD tenant
    /// depending on environment.
    /// </summary>
    public class AdalConfiguration
    {
        private const string publicAdEndpoint = "https://login.windows.net/";
        private string adEndpoint = publicAdEndpoint;

        public string AdEndpoint
        {
            get { return adEndpoint; }
            set { adEndpoint = value; }
        }

        public bool ValidateAuthority { get { return adEndpoint == publicAdEndpoint; } }

        public string AdDomain { get; set; }
        public string ClientId { get; set; }
        public Uri ClientRedirectUri { get; set; }
        public string ResourceClientUri { get; set; }

        //
        // These constants define the default values to use for AD authentication
        // against RDFE
        //
        private const string powershellClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
        private static readonly Uri powershellRedirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob");
        private const string rdfeResourceUri = "https://management.core.windows.net/";

        public static AdalConfiguration Create(WindowsAzureEnvironment environment)
        {
            return new AdalConfiguration()
            {
                AdEndpoint = environment.AdTenantUrl,
                AdDomain = environment.CommonTenantId,
                ClientId = powershellClientId,
                ClientRedirectUri = powershellRedirectUri,
                ResourceClientUri = rdfeResourceUri
            };
        }
    }
}