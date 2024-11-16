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

using System.Reflection;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using log4net;

namespace WebRtcVoice
{
    // Encapsulization of a Session to the Janus server
    public class JanusRoomAttendee
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS ROOM ATTENDEE]";

        public JanusRoom Room;
        public string OfferOrig;
        public string Offer;
        // Contains "type" and "sdp" fields
        public OSDMap Answer;

        // The simulator has a GUID to identify the user
        public string AgentId { get; set; }
        // The simulator keeps track of the user session by this unique ID
        public string AttendeeSession = UUID.Random().ToString();
        // The Janus server keeps track of the user by this ID
        public int JanusAttendeeId;

        // Keep track of an attendee in a room
        public JanusRoomAttendee(JanusRoom pRoom)
        {
            m_log.DebugFormat("{0} JanusRoomAttendee constructor", LogHeader);
            Room = pRoom;
        }
    }
}
