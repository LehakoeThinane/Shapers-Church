using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.People.Tests;

public sealed class ConsentPurposeTests
{
    [Fact]
    public void The_purposes_other_modules_check_match_the_ones_people_record()
    {
        Assert.Equal(ConsentPurposes.EmailCommunication, CommunicationConsents.Email);
        Assert.Equal(ConsentPurposes.SmsCommunication, CommunicationConsents.Sms);
        Assert.Equal(ConsentPurposes.WhatsAppCommunication, CommunicationConsents.WhatsApp);
        Assert.Equal(ConsentPurposes.PushNotifications, CommunicationConsents.Push);
    }
}
