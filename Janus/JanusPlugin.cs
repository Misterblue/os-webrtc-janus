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
using System.Threading.Tasks;

using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Base;

using OpenMetaverse.StructuredData;
using OpenMetaverse;

using Nini.Config;
using log4net;

namespace WebRtcVoice
{
    // Encapsulization of a Session to the Janus server
    public class JanusPlugin : IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS PLUGIN]";

        protected IConfigSource _Config;
        protected JanusSession _JanusSession;

        public string PluginName { get; private set; }
        public string PluginId { get; private set; }
        public string PluginUri { get ; private set ; }

        public bool IsConnected => !String.IsNullOrEmpty(PluginId);

        // Wrapper around the session connection to Janus-gateway
        public JanusPlugin(JanusSession pSession, string pPluginName)
        {
            m_log.DebugFormat("{0} JanusPlugin constructor", LogHeader);
            _JanusSession = pSession;
            PluginName = pPluginName;
        }

        public virtual void Dispose()
        {
            if (IsConnected)
            {
                // Close the handle

            }
        }

        public Task<JanusMessageResp> SendPluginMsg(OSDMap pParams)
        {
            return _JanusSession.SendToJanus(new PluginMsgReq(pParams), PluginUri);
        }
        public Task<JanusMessageResp> SendPluginMsg(PluginMsgReq pJMsg)
        {
            return _JanusSession.SendToJanus(pJMsg, PluginUri);
        }

        /// <summary>
        /// Make the create a handle to a plugin within the session.
        /// </summary>
        /// <returns>TRUE if handle was created successfully</returns>
        public async Task<bool> Activate(IConfigSource pConfig)
        {
            _Config = pConfig;

            bool ret = false;
            try
            {
                var resp = await _JanusSession.SendToSession(new AttachPluginReq(PluginName));
                if (resp is not null && resp.isSuccess)
                {
                    var handleResp = new AttachPluginResp(resp);
                    PluginId = handleResp.pluginId;
                    PluginUri = _JanusSession.SessionUri + "/" + PluginId;
                    m_log.DebugFormat("{0} Activate. Created. ID={1}, URL={2}", LogHeader, PluginId, PluginUri);
                    ret = true;
                }
                else
                {
                    m_log.ErrorFormat("{0} Activate: failed", LogHeader);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} Activate: exception {1}", LogHeader, e);
            }

            return ret;
        }

        public virtual async Task<bool> Detach()
        {
            bool ret = false;
            try
            {
                // We send the 'detach' message to the plugin URI
                var resp = await _JanusSession.SendToJanus(new DetachPluginReq(), PluginUri);
                if (resp is not null && resp.isSuccess)
                {
                    m_log.DebugFormat("{0} Detach. Detached", LogHeader);
                    ret = true;
                }
                else
                {
                    m_log.ErrorFormat("{0} Detach: failed", LogHeader);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} Detach: exception {1}", LogHeader, e);
            }

            return ret;
        }   
    }
}