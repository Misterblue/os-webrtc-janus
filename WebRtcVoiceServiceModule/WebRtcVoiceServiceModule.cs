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
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using OpenSim.Server.Base;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;

using OpenMetaverse.StructuredData;
using OpenMetaverse;
using OpenSim.Framework;

using Mono.Addins;

using log4net;
using Nini.Config;

[assembly: Addin("WebRtcVoiceServiceModule", "1.0")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

namespace WebRtcVoice
{
    /// <summary>
    /// Interface for the WebRtcVoiceService.
    /// An instance of this is registered as the IWebRtcVoiceService for this region.
    /// The function here is to direct the capability requests to the appropriate voice service.
    /// For the moment, there are separate voice services for spacial and non-spacial voice
    /// with the idea that a region could have a pre-region spacial voice service while
    /// the grid could have a non-spacial voice service for group chat, etc.
    /// Fancier configurations are possible.
    /// </summary>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "WebRtcVoiceServiceModule")]
    public class WebRtcVoiceServiceModule : ISharedRegionModule, IWebRtcVoiceService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string LogHeader = "[WEBRTC VOICE SERVICE MODULE]";

        private static bool m_Enabled = false;
        private IConfigSource m_Config;

        private IWebRtcVoiceService m_spacialVoiceService;
        private IWebRtcVoiceService m_nonSpacialVoiceService;

        // =====================================================================
        public static Dictionary<string, IVoiceViewerSession> ViewerSessions = new Dictionary<string, IVoiceViewerSession>();
        public static bool TryGetViewerSession(string pSessionId, out IVoiceViewerSession pViewerSession)
        {
            return ViewerSessions.TryGetValue(pSessionId, out pViewerSession);
        }
        public static void AddViewerSession(IVoiceViewerSession pSession)
        {
            ViewerSessions[pSession.ViewerSessionID] = pSession;
        }
        public static void RemoveViewerSession(string pSessionId)
        {
            ViewerSessions.Remove(pSessionId);
        }
        // =====================================================================

        // ISharedRegionModule.Initialize
        // Get configuration and load the modules that will handle spacial and non-spacial voice.
        public void Initialise(IConfigSource pConfig)
        {
            m_log.DebugFormat("{0} WebRtcVoiceServiceModule constructor", LogHeader);
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

        // ISharedRegionModule.PostInitialize
        public void PostInitialise()
        {
        }

        // ISharedRegionModule.Close
        public void Close()
        {
        }

        // ISharedRegionModule.ReplaceableInterface
        public Type ReplaceableInterface
        {
            get { return null; }
        }

        // ISharedRegionModule.Name
        public string Name
        {
            get { return "WebRtcVoiceServiceModule"; }
        }

        // ISharedRegionModule.AddRegion
        public void AddRegion(Scene scene)
        {
            if (m_Enabled)
            {
                m_log.DebugFormat("{0} Adding WebRtcVoiceService to region {1}", LogHeader, scene.Name);
                scene.RegisterModuleInterface<IWebRtcVoiceService>(this);
            }

        }

        // ISharedRegionModule.RemoveRegion
        public void RemoveRegion(Scene scene)
        {
            if (m_Enabled)
            {
                scene.UnregisterModuleInterface<IWebRtcVoiceService>(this);
            }
        }

        // ISharedRegionModule.RegionLoaded
        public void RegionLoaded(Scene scene)
        {
        }

        // =====================================================================
        // IWebRtcVoiceService

        // IWebRtcVoiceService.ProvisionVoiceAccountRequest
        public async Task<OSDMap> ProvisionVoiceAccountRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            OSDMap response = null;
            IVoiceViewerSession vSession = null;
            if (pRequest.ContainsKey("viewer_session"))
            {
                // request has a viewer session. Use that to find the voice service
                string viewerSessionId = pRequest["viewer_session"].AsString();
                if (TryGetViewerSession(viewerSessionId, out vSession))
                {
                    response = await vSession.VoiceService.ProvisionVoiceAccountRequest(vSession, pRequest, pUserID, pScene);
                }
                else
                {
                    m_log.ErrorFormat("{0} ProvisionVoiceAccountRequest: viewer session {1} not found", LogHeader, viewerSessionId);
                }
            }   
            else
            {
                // the request does not have a viewer session. See if it's an initial request
                if (pRequest.ContainsKey("channel_type"))
                {
                    string channelType = pRequest["channel_type"].AsString();
                    if (channelType == "local")
                    {
                        vSession = m_spacialVoiceService.CreateViewerSession(pRequest, pUserID, pScene);
                        AddViewerSession(vSession);
                        response = await m_spacialVoiceService.ProvisionVoiceAccountRequest(vSession, pRequest, pUserID, pScene);
                    }
                    else
                    {
                        vSession = m_nonSpacialVoiceService.CreateViewerSession(pRequest, pUserID, pScene);
                        AddViewerSession(vSession);
                        response = await m_nonSpacialVoiceService.ProvisionVoiceAccountRequest(vSession, pRequest, pUserID, pScene);
                    }
                }
                else
                {
                    m_log.ErrorFormat("{0} ProvisionVoiceAccountRequest: no channel_type in request", LogHeader);
                }
            }
            return response;
        }

        // IWebRtcVoiceService.VoiceSignalingRequest
        public async Task<OSDMap> VoiceSignalingRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            OSDMap response = null;
            IVoiceViewerSession vSession = null;
            if (pRequest.ContainsKey("viewer_session"))
            {
                // request has a viewer session. Use that to find the voice service
                string viewerSessionId = pRequest["viewer_session"].AsString();
                if (TryGetViewerSession(viewerSessionId, out vSession))
                {
                    response = await vSession.VoiceService.VoiceSignalingRequest(vSession, pRequest, pUserID, pScene);
                }
                else
                {
                    m_log.ErrorFormat("{0} VoiceSignalingRequest: viewer session {1} not found", LogHeader, viewerSessionId);
                }
            }   
            else
            {
                m_log.ErrorFormat("{0} VoiceSignalingRequest: no viewer_session in request", LogHeader);
            }
            return response;
        }

        // This module should never be called with this signature
        public Task<OSDMap> ProvisionVoiceAccountRequest(IVoiceViewerSession pVSession, OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        // This module should never be called with this signature
        public Task<OSDMap> VoiceSignalingRequest(IVoiceViewerSession pVSession, OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        public IVoiceViewerSession CreateViewerSession(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }
    }
}
