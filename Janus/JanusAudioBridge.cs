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
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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

        public async Task<JanusRoom> CreateRoom(int pRoomId, bool pSpacial, string pRoomDesc)
        {
            JanusRoom ret = null;
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeCreateRoomReq(pRoomId, pSpacial, pRoomDesc));
                AudioBridgeResp abResp = new AudioBridgeResp(resp);

                m_log.DebugFormat("{0} CreateRoom. ReturnCode: {1}", LogHeader, abResp.AudioBridgeReturnCode);
                switch (abResp.AudioBridgeReturnCode)
                {
                    case "created":
                        ret = new JanusRoom(this, pRoomId);
                        break;
                    case "event":
                        if (abResp.AudioBridgeErrorCode == 486)
                        {
                            m_log.ErrorFormat("{0} CreateRoom. Room {1} already exists {2}", LogHeader, pRoomId, abResp.ToString());
                            // if room already exists, just use it
                            ret = new JanusRoom(this, pRoomId);
                        }
                        else
                        {
                            m_log.ErrorFormat("{0} CreateRoom. XX Room creation failed: {1}", LogHeader, abResp.ToString());
                        }
                        break;
                    default:
                        m_log.ErrorFormat("{0} CreateRoom. YY Room creation failed: {1}", LogHeader, abResp.ToString());
                        break;
                }   
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} CreateRoom. Exception {1}", LogHeader, e);
            }
            return ret;
        }

        public async Task<bool> DestroyRoom(JanusRoom janusRoom)
        {
            bool ret = false;
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeDestroyRoomReq(janusRoom.RoomId));
                ret = true;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} DestroyRoom. Exception {1}", LogHeader, e);
            }
            return ret;
        }

        public async Task<bool> JoinRoom(JanusRoom janusRoom, UUID pAgentID, string pAgentName)
        {
            bool ret = false;
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeJoinRoomReq(janusRoom.RoomId, pAgentName));
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} JoinRoom. Exception {1}", LogHeader, e);
            }
            return ret;
        }

        public async Task<bool> LeaveRoom(JanusRoom janusRoom, UUID pAgentID)
        {
            bool ret = false;
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeLeaveRoomReq(janusRoom.RoomId));
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} LeaveRoom. Exception {1}", LogHeader, e);
            }
            return ret;
        }

        // Constant used to denote that this is a spacial audio room for the region (as opposed to parcels)
        public const int REGION_ROOM_ID = -999;
        private Dictionary<string, JanusRoom> _rooms = new Dictionary<string, JanusRoom>();
        private int _regionRoomID = 10;
        // Return a room for the given channel type and parcel ID. If the room already exists, return it.
        public async Task<JanusRoom> SelectRoom(string pChannelType, bool pSpacial, int pParcelLocalID, string pChannelID)
        {
            // A string that contains the room differentiator. Should be unique for each type of room
            string roomDiffereniator = $"{pChannelType}-{pParcelLocalID}-{pChannelID}";
            m_log.DebugFormat("{0} SelectRoom: diff={1}", LogHeader, roomDiffereniator);

            // Check to see if the room has already been created
            lock (_rooms)
            {
                if (_rooms.ContainsKey(roomDiffereniator))
                {
                    return _rooms[roomDiffereniator];
                }
            }

            // The room doesn't exist. Create it.
            JanusRoom ret = await CreateRoom(_regionRoomID++, pSpacial, roomDiffereniator);

            JanusRoom existingRoom = null;
            if (ret is not null)
            {
                lock (_rooms)
                {
                    if (_rooms.ContainsKey(roomDiffereniator))
                    {
                        // If the room was created while we were waiting, 
                        existingRoom = _rooms[roomDiffereniator];
                    }
                    else
                    {
                        // Our room is the first one created. Save it.
                        _rooms[roomDiffereniator] = ret;
                    }
                }
            }
            if (existingRoom is not null)
            {
                // The room we created was already created by someone else. Delete ours and use the existing one
                await DestroyRoom(ret);
                ret = existingRoom;
            }
            return ret;
        }

        // Return the room with the given room ID or 'null' if no such room
        public JanusRoom GetRoom(int pRoomId)
        {
            JanusRoom ret = null;
            lock (_rooms)
            {
                foreach (var room in _rooms)
                {
                    if (room.Value.RoomId == pRoomId)
                    {
                        ret = room.Value;
                        break;
                    }
                }
            }
            return ret;
        }   
    }
}