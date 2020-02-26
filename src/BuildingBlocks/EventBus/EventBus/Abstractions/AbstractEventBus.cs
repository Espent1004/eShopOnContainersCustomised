using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Extensions;
using Newtonsoft.Json;

namespace Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions
{
    public abstract class AbstractEventBus : IEventBus
    {
        private static readonly String tenantACustomisationUrl = @"http://tenantacustomisation/";
        private static readonly String tenantManagerUrl = @"http://tenantmanager/";

        protected AbstractEventBus()
        {
        }

        protected async void SendEventToTenant(String content, String id, String eventName, String url)
        {
            var temp = new SavedEvent();
            temp.Content = content;
            temp.SavedEventId = id;
            temp.EventName = eventName;
            string myJson = JsonConvert.SerializeObject(temp);
            using (var client = new HttpClient())
            {
                try
                {
                    var response = await client.PostAsync(
                        url,
                        new StringContent(myJson, Encoding.UTF8, "application/json"));
                    response.EnsureSuccessStatusCode();
                }

                catch (Exception e)
                {
                }
            }
        }

        protected async Task<String> GetCustomisation(String eventName, int tenantId)
        {
            CustomisationInfo customisationInfo = new CustomisationInfo();
            customisationInfo.EventName = eventName;
            customisationInfo.TenantId = tenantId;
            String customisationUrl = null;

            var builder = new UriBuilder(tenantManagerUrl + "api/Customisations/IsCustomised");
            builder.Port = -1;
            var query = HttpUtility.ParseQueryString(builder.Query);
            query["eventName"] = eventName;
            query["tenantId"] = tenantId.ToString();
            builder.Query = query.ToString();
            string url = builder.ToString();

            using (var client = new HttpClient())
            {
                try
                {
                    var response = await client.GetAsync(
                        url);
                    response.EnsureSuccessStatusCode();
                    customisationUrl = response.Content.ReadAsStringAsync().Result;
                }
                catch (Exception e)
                {
                }
            }

            return customisationUrl;
        }

        public void Publish(IntegrationEvent @event)
        {
            if (@event.CheckForCustomisation)
            {
                var customisationUrl = GetCustomisation(@event.GetGenericTypeName(), @event.TenantId).Result;
                if (!String.IsNullOrEmpty(customisationUrl))
                {
                    //Checking if event should be sent to tenant, or handled normally
                    SendEventToTenant(JsonConvert.SerializeObject(@event), @event.Id.ToString(), @event.GetGenericTypeName(),
                        customisationUrl);
                    return;
                }
            }
            ActualPublish(@event);
        }

        protected abstract void ActualPublish(IntegrationEvent @event);
        public abstract void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>;
        public abstract void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler;
        public abstract void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler;
        public abstract void Unsubscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>;
        public abstract string GetVHost();
    }
}

class SavedEvent
{
    public string SavedEventId { get; set; }
    public string Content { get; set; }
    public String EventName { get; set; }
}

class CustomisationInfo
{
    public string EventName { get; set; }
    public int TenantId { get; set; }
}