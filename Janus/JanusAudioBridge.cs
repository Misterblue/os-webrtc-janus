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
    public class JanusAudioBridge : JanusPlugin
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS AUDIO BRIDGE]";

        // Wrapper around the session connection to Janus-gateway
        public JanusAudioBridge(JanusSession pSession) : base(pSession, "janus.plugin.audiobridge")
        {
            m_log.DebugFormat("{0} JanusAudioBridge constructor", LogHeader);
        }

        public override void Dispose()
        {
            if (IsConnected)
            {
                // Close the handle

            }
        }

        public async Task<JanusRoom> CreateRoom(string pRoomId)
        {
            JanusRoom ret = null;
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeCreateReq(pRoomId));
                // TODO: create JanusRoom object
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} JoinRoom. Exception {1}", LogHeader, e);
            }

            return ret;
        }

        public async Task<bool> DestroyRoom(JanusRoom janusRoom)
        {
            bool ret = false;
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeDestroyReq(janusRoom.RoomId));
                ret = true;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} DestroyRoom. Exception {1}", LogHeader, e);
            }
            return ret;
        }

    }
}
