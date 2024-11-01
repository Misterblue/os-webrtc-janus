// Copyright 2024 Robert Adams (misterblue@misterblue.com)
//
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Reflection;

using OpenSim.Server.Base;
using OpenSim.Services.Base;
using OpenSim.Services.Interfaces;

using OpenMetaverse.StructuredData;
using OpenMetaverse;
using OpenSim.Framework;

using Nini.Config;
using log4net;

namespace OpenSim.Services.WebRtcVoiceService
{
    public class WebRtcVoiceService : ServiceBase, IWebRtcVoiceService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[WEBRTC VOICE SERVICE]";

        private readonly IConfigSource m_Config;
        private bool m_Enabled = false;

        private IWebRtcVoiceService m_spacialVoiceService;
        private IWebRtcVoiceService m_nonSpacialVoiceService;

        public WebRtcVoiceService(IConfigSource pConfig) : base(pConfig)
        {
            m_log.DebugFormat("{0} WebRtcVoiceService constructor", LogHeader);
            m_Config = pConfig;
            IConfig moduleConfig = m_Config.Configs["WebRtcVoice"];

            if (moduleConfig is not null)
            {
                m_Enabled = moduleConfig.GetBoolean("Enabled", false);
                if (m_Enabled)
                {
                    // Get the DLLs for the two voice services
                    string spacialDllName = moduleConfig.GetString("SpacialVoiceService", String.Empty);
                    string nonSpacialDllName = moduleConfig.GetString("NonSpacialVoiceService", String.Empty);
                    if (String.IsNullOrEmpty(spacialDllName) && String.IsNullOrEmpty(nonSpacialDllName))
                    {
                        m_log.ErrorFormat("{0} No SpacialVoiceService or NonSpacialVoiceService specified in configuration", LogHeader);
                        m_Enabled = false;
                    }

                    // Default non-spacial to spacial if not specified
                    if (String.IsNullOrEmpty(nonSpacialDllName))
                    {
                        m_log.DebugFormat("{0} nonSpacialDllName not specified. Defaulting to spacialDllName", LogHeader);
                        nonSpacialDllName = spacialDllName;
                    }

                    // Load the two voice services
                    m_log.DebugFormat("{0} Loading SpacialVoiceService from {1}", LogHeader, spacialDllName);
                    m_spacialVoiceService = ServerUtils.LoadPlugin<IWebRtcVoiceService>(spacialDllName, new object[] { m_Config });
                    if (m_spacialVoiceService is null)
                    {
                        m_log.ErrorFormat("{0} Could not load SpacialVoiceService from {1}", LogHeader, spacialDllName);
                        m_Enabled = false;
                    }

                    m_log.DebugFormat("{0} Loading NonSpacialVoiceService from {1}", LogHeader, nonSpacialDllName);
                    m_nonSpacialVoiceService = ServerUtils.LoadPlugin<IWebRtcVoiceService>(nonSpacialDllName, new object[] { m_Config });
                    if (m_nonSpacialVoiceService is null)
                    {
                        m_log.ErrorFormat("{0} Could not load NonSpacialVoiceService from {1}", LogHeader, nonSpacialDllName);
                        m_Enabled = false;
                    }

                    if (m_Enabled)
                    {
                        m_log.InfoFormat("{0} WebRtcVoiceService enabled", LogHeader);
                    }
                }
            }
        }

        public OSDMap ProvisionVoiceAccountRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new System.NotImplementedException();
        }

        public OSDMap VoiceSignalingRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new System.NotImplementedException();
        }
    }
}

