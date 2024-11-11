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
    public class JanusRoom : IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS ROOM]";

        public string RoomId { get; private set; }
        public string RoomUri { get; private set; }
        private bool IsConnected => !String.IsNullOrEmpty(RoomId);

        private JanusPlugin _AudioBridge;

        // Wrapper around the session connection to Janus-gateway
        public JanusRoom(JanusPlugin pAudioBridge)
        {
            m_log.DebugFormat("{0} JanusRoom constructor", LogHeader);
            _AudioBridge = pAudioBridge;
        }

        public void Dispose()
        {
            if (IsConnected)
            {
                // Close the room
            }
        }

        /// <summary>
        /// Create a room in this audio bridge
        /// </summary>
        /// <returns>TRUE if room was created successfully</returns>
        public async Task<bool> CreateRoom(string pPluginName)
        {
            bool ret = false;
            try
            {
                /*
                var resp = await _AudioBridge.PostToSession(new AttachPluginReq(pPluginName));
                if (resp is not null && resp.isSuccess)
                {
                    var handleResp = new AttachPluginResp(resp);
                    HandleId = handleResp.pluginId;
                    HandleUri = _JanusSession.SessionUri + "/" + HandleId;
                    m_log.DebugFormat("{0} CreateRoom. Created. ID={1}, URL={2}", LogHeader, HandleId, HandleUri);
                    ret = true;
                }
                else
                {
                    m_log.ErrorFormat("{0} CreateRoom: failed", LogHeader);
                }
                */
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} CreateRoom: exception {1}", LogHeader, e);
            }

            return ret;
        }
    }
}
