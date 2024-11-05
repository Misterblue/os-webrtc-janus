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
using System.Net.Http;
using System.Net.Mime;

namespace WebRtcVoice
{
    public class JanusComm : IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS COMM]";
        private string _JanusServerURI = String.Empty;
        private string _JanusAPIToken = String.Empty;
        private string _JanusAdminURI = String.Empty;
        private string _JanusAdminToken = String.Empty;

        public string JanusServerURI => _JanusServerURI;
        public string JanusAdminURI => _JanusAdminURI;

        private HttpClient _HttpClient = new HttpClient();

        public JanusComm(string pServerURI, string pAPIToken, string pAdminURI, string pAdminToken)
        {
            m_log.DebugFormat("{0} JanusSession constructor", LogHeader);
            _JanusServerURI = pServerURI;
            _JanusAPIToken = pAPIToken;
            _JanusAdminURI = pAdminURI;
            _JanusAdminToken = pAdminToken;
        }

        public void Dispose()
        {
            if (_HttpClient is not null)
            {
                _HttpClient.Dispose();
                _HttpClient = null;
            }
        }

        public async Task<JanusMessageResp> PostToJanus(JanusMessageReq pReq)
        {
            return await PostToJanus(pReq, _JanusServerURI);
        }

        public async Task<JanusMessageResp> PostToJanus(JanusMessageReq pReq, string pURI)
        {
            m_log.DebugFormat("{0} PostToJanus", LogHeader);

            if (!String.IsNullOrEmpty(_JanusAPIToken))
            {
                pReq.AddAPIToken(_JanusAPIToken);
            }

            JanusMessageResp ret = null;
            try
            {
                m_log.DebugFormat("{0} PostToJanus: request {1}", LogHeader, pReq.ToJson());

                HttpRequestMessage reqMsg = new HttpRequestMessage(HttpMethod.Post, pURI);
                string reqStr = pReq.ToJson();
                reqMsg.Content = new StringContent(reqStr, System.Text.Encoding.UTF8, MediaTypeNames.Application.Json);
                reqMsg.Headers.Add("Accept", "application/json");
                HttpResponseMessage response = await _HttpClient.SendAsync(reqMsg);

                if (response.IsSuccessStatusCode)
                {
                    string respStr = await response.Content.ReadAsStringAsync();
                    ret = JanusMessageResp.FromJson(respStr);
                    if (ret.isSuccess)
                    {
                        m_log.DebugFormat("{0} PostToJanus: response {1}", LogHeader, respStr);
                    }
                    else
                    {
                        m_log.ErrorFormat("{0} PostToJanus: response not successful {1}", LogHeader, respStr);
                    }
                }
                else
                {
                    m_log.ErrorFormat("{0} PostToJanus: response not successful {1}", LogHeader, response);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} PostToJanus: exception {1}", LogHeader, e);
            }

            return ret;
        }

        public Task<JanusMessageResp> PostToJanusAdmin(JanusMessageReq pReq)
        {
            return PostToJanus(pReq, _JanusAdminURI);
        }

        public Task<JanusMessageResp> GetFromJanus()
        {
            return GetFromJanus(_JanusServerURI);
        }
        public async Task<JanusMessageResp> GetFromJanus(string pURI)
        {
            m_log.DebugFormat("{0} GetFromJanus", LogHeader);

            if (!String.IsNullOrEmpty(_JanusAPIToken))
            {
                pURI += "?apisecret=" + _JanusAPIToken;
            }

            JanusMessageResp ret = null;
            try
            {
                m_log.DebugFormat("{0} GetFromJanus: URI = \"{1}\"", LogHeader, pURI);
                HttpRequestMessage reqMsg = new HttpRequestMessage(HttpMethod.Get, pURI);
                reqMsg.Headers.Add("Accept", "application/json");
                HttpResponseMessage response = await _HttpClient.SendAsync(reqMsg);

                if (response.IsSuccessStatusCode)
                {
                    string respStr = await response.Content.ReadAsStringAsync();
                    ret = JanusMessageResp.FromJson(respStr);
                    m_log.DebugFormat("{0} GetFromJanus: response {1}", LogHeader, respStr);
                }
                else
                {
                    m_log.ErrorFormat("{0} GetFromJanus: response not successful {1}", LogHeader, response);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} GetFromJanus: exception {1}", LogHeader, e);
            }

            return ret;
        }
    }
}
