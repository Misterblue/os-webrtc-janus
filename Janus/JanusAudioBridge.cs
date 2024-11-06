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

using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Base;

using OpenMetaverse.StructuredData;
using OpenMetaverse;

using Nini.Config;
using log4net;
using System.Threading.Tasks;

namespace WebRtcVoice
{
    // Encapsulization of a Session to the Janus server
    public class JanusAudioBridge : IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS AUDIO BRIDGE]";

        private IConfigSource _Config;

        private JanusSession _JanusSession;

        public string HandleId { get; private set; }
        public string HandleUri { get ; private set ; }

        public bool IsConnected => !String.IsNullOrEmpty(HandleId);

        // Wrapper around the session connection to Janus-gateway
        public JanusAudioBridge(JanusSession pSession)
        {
            m_log.DebugFormat("{0} JanusAudioBridge constructor", LogHeader);
            _JanusSession = pSession;
        }

        public void Dispose()
        {
            if (IsConnected)
            {
                // Close the handle

            }
        }

        public async Task<bool> Activate(IConfigSource pConfig)
        {
            _Config = pConfig;
            
            bool ret = false;
            try
            {
                JanusPluginHandle janusPluginHandle = new JanusPluginHandle(_JanusSession);
                if (await janusPluginHandle.AttachPlugin("janus.plugin.audiobridge"))
                {
                    HandleId = janusPluginHandle.HandleId;
                    HandleUri = janusPluginHandle.HandleUri;
                    m_log.DebugFormat("{0} Activate. Created audiobridge plugin handle. Uri={1}", LogHeader, HandleUri);
                    ret = true;
                }
                else
                {
                    m_log.ErrorFormat("{0} Activate. Failed to create plugin handle", LogHeader);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} Activate. Exception {1}", LogHeader, e);
            }

            return ret;
        }

    }
}
