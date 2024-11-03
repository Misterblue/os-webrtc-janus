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
using OpenSim.Services.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;

using Mono.Addins;

using log4net;
using Nini.Config;

namespace WebRtcVoice
{
    /// <summary>
    /// This module exists to load the WebRtcVoiceService into the region.
    /// It initially loads the dll via [WebRtcVoice]BaseService in the configuration
    /// then, as regions are added, registers the WebRtcVoiceService with the region.
    /// </summary>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "WebRtcVoiceServiceModule")]
    public class WebRtcVoiceServiceModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string LogHeader = "[WEBRTC VOICE SERVICE MODULE]";

        private static bool m_Enabled = false;
        private IConfigSource m_Config;

        private IWebRtcVoiceService m_WebRtcVoiceService;

        // ISharedRegionModule.Initialize
        public void Initialise(IConfigSource config)
        {
            m_Config = config;
            IConfig moduleConfig = config.Configs["WebRtcVoice"];

            if (moduleConfig is not null)
            {
                m_Enabled = moduleConfig.GetBoolean("Enabled", false);
                if (m_Enabled)
                {
                    string dllName = moduleConfig.GetString("BaseService", String.Empty);
                    if (String.IsNullOrEmpty(dllName))
                    {
                        m_log.ErrorFormat("{0} No BaseService specified in configuration", LogHeader);
                        m_Enabled = false;
                    }
                    else
                    {
                        m_log.DebugFormat("{0} Loading WebRtcVoiceService from {1}", LogHeader, dllName);
                        m_WebRtcVoiceService = ServerUtils.LoadPlugin<IWebRtcVoiceService>(dllName, new object[] { config });
                        m_log.InfoFormat("{0} WebRtcVoiceModule enabled", LogHeader);
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
            if (m_Enabled && m_WebRtcVoiceService is not null)
            {
                m_log.DebugFormat("{0} Adding WebRtcVoiceService to region {1}", LogHeader, scene.Name);
                scene.RegisterModuleInterface<IWebRtcVoiceService>(m_WebRtcVoiceService);
            }

        }

        // ISharedRegionModule.RemoveRegion
        public void RemoveRegion(Scene scene)
        {
            if (m_Enabled && m_WebRtcVoiceService is not null)
            {
                scene.UnregisterModuleInterface<IWebRtcVoiceService>(m_WebRtcVoiceService);
            }
        }

        // ISharedRegionModule.RegionLoaded
        public void RegionLoaded(Scene scene)
        {
        }
    }
}
