using Microsoft.AspNetCore.SignalR;
using Moq;
using WowzaSample.Hubs;
using WowzaSample.Models;

using Xunit;

namespace WowzaSample.Tests.Hubs;

public class WebRTCHubTests
{
    private const string CallerId = "conn-caller";
    private const string TargetId = "conn-target";

    /// <summary>
    /// Creates a WebRTCHub wired to mocks, plus the shared in-memory collections.
    /// Returns a builder so each test can customize before calling .Build().
    /// </summary>
    private static HubBuilder CreateHub(string connectionId) => new(connectionId);

    private sealed class HubBuilder(string connectionId)
    {
        public List<User> Users { get; } = [];
        public List<UserCall> UserCalls { get; } = [];
        public List<CallOffer> CallOffers { get; } = [];

        public Mock<IHubCallerClients<IWebRTCHub>> ClientsMock { get; } = new();
        public Mock<IWebRTCHub> AllProxy { get; } = new();
        public Mock<IWebRTCHub> CallerProxy { get; } = new();
        public Dictionary<string, Mock<IWebRTCHub>> ClientProxies { get; } = new();

        public void AddUser(string username, string connId)
        {
            Users.Add(new User { Username = username, ConnectionId = connId });
        }

        public void AddCall(string callerConnId, string targetConnId)
        {
            var caller = Users.FirstOrDefault(u => u.ConnectionId == callerConnId)
                         ?? new User { Username = callerConnId, ConnectionId = callerConnId };
            var target = Users.FirstOrDefault(u => u.ConnectionId == targetConnId)
                         ?? new User { Username = targetConnId, ConnectionId = targetConnId };
            UserCalls.Add(new UserCall
            {
                Users = [caller, target]
            });
        }

        public void AddOffer(string callerConnId, string targetConnId)
        {
            var caller = Users.FirstOrDefault(u => u.ConnectionId == callerConnId)
                         ?? new User { Username = callerConnId, ConnectionId = callerConnId };
            var target = Users.FirstOrDefault(u => u.ConnectionId == targetConnId)
                         ?? new User { Username = targetConnId, ConnectionId = targetConnId };
            CallOffers.Add(new CallOffer { Caller = caller, Callee = target });
        }

        public WebRTCHub Build()
        {
            ClientsMock.Setup(c => c.All).Returns(AllProxy.Object);
            ClientsMock.Setup(c => c.Caller).Returns(CallerProxy.Object);
            ClientsMock
                .Setup(c => c.Client(It.IsAny<string>()))
                .Returns<string>(id =>
                {
                    if (!ClientProxies.ContainsKey(id))
                        ClientProxies[id] = new Mock<IWebRTCHub>(MockBehavior.Default);
                    return ClientProxies[id].Object;
                });

            var contextMock = new Mock<HubCallerContext>();
            contextMock.Setup(c => c.ConnectionId).Returns(connectionId);

            var groupsMock = new Mock<IGroupManager>();

            var hub = new WebRTCHub(Users, UserCalls, CallOffers)
            {
                Clients = ClientsMock.Object,
                Context = contextMock.Object,
                Groups = groupsMock.Object
            };
            return hub;
        }
    }

    // ────────────────────────────── Join ──────────────────────────────

    [Fact]
    public async Task Join_AddsUserAndBroadcastsUpdate()
    {
        var b = CreateHub(CallerId);
        var hub = b.Build();

        await hub.Join("Alice");

        var user = Assert.Single(b.Users);
        Assert.Equal("Alice", user.Username);
        Assert.Equal(CallerId, user.ConnectionId);
        Assert.False(user.InCall);

        b.AllProxy.Verify(p => p.updateUserList(b.Users), Times.Once);
    }

    [Fact]
    public async Task Join_AppendsToExistingUsers()
    {
        var b = CreateHub(TargetId);
        b.AddUser("Alice", CallerId);
        var hub = b.Build();

        await hub.Join("Bob");

        Assert.Equal(2, b.Users.Count);
        Assert.Equal("Bob", b.Users[1].Username);
    }

    // ────────────────────────────── CallUser ──────────────────────────────

    [Fact]
    public async Task CallUser_WithValidTarget_SendsIncomingCallAndCreatesOffer()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        var hub = b.Build();

        await hub.CallUser(new User { ConnectionId = TargetId });

        // Offer created
        var offer = Assert.Single(b.CallOffers);
        Assert.Equal(CallerId, offer.Caller.ConnectionId);
        Assert.Equal(TargetId, offer.Callee.ConnectionId);

        // Target received incoming call
        var targetProxy = b.ClientProxies[TargetId];
        targetProxy.Verify(
            p => p.incomingCall(It.Is<User>(u => u.Username == "Alice")),
            Times.Once);

        // Caller was NOT notified
        b.CallerProxy.Verify(p => p.callDeclined(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CallUser_WhenTargetNotFound_SendsDeclinedToCaller()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        // Bob is NOT in the user list
        var hub = b.Build();

        var targetArg = new User { ConnectionId = "nonexistent" };
        await hub.CallUser(targetArg);

        Assert.Empty(b.CallOffers);
        b.CallerProxy.Verify(
            p => p.callDeclined(targetArg, "The user you called has left."),
            Times.Once);
    }

    [Fact]
    public async Task CallUser_WhenTargetIsInCall_SendsDeclined()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddCall(TargetId, "someone-else");
        var hub = b.Build();

        await hub.CallUser(new User { ConnectionId = TargetId });

        Assert.Empty(b.CallOffers);
        b.CallerProxy.Verify(
            p => p.callDeclined(
                It.IsAny<User>(),
                It.Is<string>(msg => msg.Contains("already in a call"))),
            Times.Once);
    }

    [Fact]
    public async Task CallUser_WhenCallerNotInUserList_DoesNotCreateOffer()
    {
        // When the calling user is not in the user list, the hub does NOT send
        // an incomingCall notification NOR create an offer (the caller is unknown).
        var b = CreateHub(CallerId);
        b.AddUser("Bob", TargetId);
        var hub = b.Build();

        await hub.CallUser(new User { ConnectionId = TargetId });

        // No offer should be created since the caller is unknown
        Assert.Empty(b.CallOffers);

        // Target does NOT receive incomingCall
        Assert.False(b.ClientProxies.ContainsKey(TargetId));
    }

    // ────────────────────────────── AnswerCall ──────────────────────────────

    [Fact]
    public async Task AnswerCall_Accept_CreatesCallAndNotifiesOriginalCaller()
    {
        var b = CreateHub(TargetId); // TargetId is the answerer
        b.AddUser("Alice", CallerId); // original caller
        b.AddUser("Bob", TargetId);   // answerer
        b.AddOffer(CallerId, TargetId);
        var hub = b.Build();

        // Bob (answerer) accepts Alice's call
        await hub.AnswerCall(true, new User { ConnectionId = CallerId });

        // UserCall created
        var call = Assert.Single(b.UserCalls);
        Assert.Contains(call.Users, u => u.ConnectionId == CallerId);
        Assert.Contains(call.Users, u => u.ConnectionId == TargetId);

        // Offer removed
        Assert.Empty(b.CallOffers);

        // Alice (original caller) notified
        var callerProxy = b.ClientProxies[CallerId];
        callerProxy.Verify(
            p => p.callAccepted(It.Is<User>(u => u.Username == "Bob")),
            Times.Once);

        // User list updated
        b.AllProxy.Verify(p => p.updateUserList(b.Users), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnswerCall_Decline_SendsDeclinedToOriginalCaller()
    {
        var b = CreateHub(TargetId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddOffer(CallerId, TargetId);
        var hub = b.Build();

        await hub.AnswerCall(false, new User { ConnectionId = CallerId });

        Assert.Empty(b.UserCalls);

        var callerProxy = b.ClientProxies[CallerId];
        callerProxy.Verify(
            p => p.callDeclined(
                It.Is<User>(u => u.Username == "Bob"),
                It.Is<string>(msg => msg.Contains("did not accept"))),
            Times.Once);
    }

    [Fact]
    public async Task AnswerCall_WhenCallerLeft_SendsCallEnded()
    {
        var b = CreateHub(TargetId);
        b.AddUser("Bob", TargetId);
        // Alice (original caller) is NOT in the list anymore
        b.AddOffer(CallerId, TargetId);
        var hub = b.Build();

        await hub.AnswerCall(true, new User { ConnectionId = CallerId });

        Assert.Empty(b.UserCalls);
        b.CallerProxy.Verify(
            p => p.callEnded(
                It.IsAny<User>(),
                It.Is<string>(msg => msg.Contains("has left"))),
            Times.Once);
    }

    [Fact]
    public async Task AnswerCall_WhenNoOfferExists_SendsCallEnded()
    {
        var b = CreateHub(TargetId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        // No offer created
        var hub = b.Build();

        await hub.AnswerCall(true, new User { ConnectionId = CallerId });

        Assert.Empty(b.UserCalls);
        b.CallerProxy.Verify(
            p => p.callEnded(
                It.IsAny<User>(),
                It.Is<string>(msg => msg.Contains("hung up"))),
            Times.Once);
    }

    [Fact]
    public async Task AnswerCall_WhenOriginalCallerAlreadyInCall_SendsDeclined()
    {
        // The hub checks whether the ORIGINAL CALLER (targetUser) is in a call,
        // not the answerer. This test covers that existing code path.
        var b = CreateHub(TargetId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddOffer(CallerId, TargetId);
        // Alice (original caller) is already in a call with Charlie
        b.AddCall(CallerId, "conn-charlie");
        var hub = b.Build();

        await hub.AnswerCall(true, new User { ConnectionId = CallerId });

        // The new call should NOT be created
        Assert.Single(b.UserCalls); // Only the pre-existing Charlie call

        b.CallerProxy.Verify(
            p => p.callDeclined(
                It.IsAny<User>(),
                It.Is<string>(msg => msg.Contains("accept someone elses call"))),
            Times.Once);
    }

    [Fact]
    public async Task AnswerCall_WhenAnsweringUserNotFound_SendsCallEnded()
    {
        var b = CreateHub(CallerId);
        // Answering user not in list (Alice answers, but Bob isn't in the list)
        b.AddUser("Alice", CallerId);
        b.AddOffer(CallerId, TargetId);
        var hub = b.Build();

        await hub.AnswerCall(true, new User { ConnectionId = TargetId });

        Assert.Empty(b.UserCalls);
        // Hub sends callEnded because the other party has left
        b.CallerProxy.Verify(
            p => p.callEnded(
                It.IsAny<User>(),
                It.Is<string>(msg => msg.Contains("has left"))),
            Times.Once);
    }

    // ────────────────────────────── HangUp ──────────────────────────────

    [Fact]
    public async Task HangUp_WhenInCall_EndsCallAndNotifiesPeer()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddCall(CallerId, TargetId);
        var hub = b.Build();

        await hub.HangUp();

        // Call removed
        Assert.Empty(b.UserCalls);

        // Peer notified
        var targetProxy = b.ClientProxies[TargetId];
        targetProxy.Verify(
            p => p.callEnded(
                It.Is<User>(u => u.Username == "Alice"),
                It.Is<string>(msg => msg.Contains("hung up"))),
            Times.Once);

        // User list updated
        b.AllProxy.Verify(p => p.updateUserList(b.Users), Times.AtLeastOnce);
    }

    [Fact]
    public async Task HangUp_WhenNotInCall_RemovesOffersOnly()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddOffer(CallerId, TargetId);
        var hub = b.Build();

        await hub.HangUp();

        // Offers cleaned up
        Assert.Empty(b.CallOffers);
        // No calls to clean up
        Assert.Empty(b.UserCalls);

        b.AllProxy.Verify(p => p.updateUserList(b.Users), Times.Once);
    }

    [Fact]
    public async Task HangUp_WhenUserNotFound_DoesNothing()
    {
        var b = CreateHub(CallerId);
        var hub = b.Build();

        // No users at all — should not throw
        var ex = await Record.ExceptionAsync(() => hub.HangUp());
        Assert.Null(ex);

        b.AllProxy.Verify(p => p.updateUserList(It.IsAny<List<User>>()), Times.Never);
    }

    [Fact]
    public async Task HangUp_RemovesAllOffersInitiatedByCaller()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddOffer(CallerId, TargetId);
        b.AddOffer(CallerId, "conn-charlie"); // second offer from Alice
        var hub = b.Build();

        await hub.HangUp();

        Assert.Empty(b.CallOffers);
    }

    // ────────────────────────────── SendSignal ──────────────────────────────

    [Fact]
    public async Task SendSignal_WhenInSameCall_ForwardsSignalToTarget()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddCall(CallerId, TargetId);
        var hub = b.Build();

        await hub.SendSignal("SDP_OFFER", TargetId);

        var targetProxy = b.ClientProxies[TargetId];
        targetProxy.Verify(
            p => p.receiveSignal(
                It.Is<User>(u => u.Username == "Alice"),
                "SDP_OFFER"),
            Times.Once);
    }

    [Fact]
    public async Task SendSignal_WhenNotInCall_DoesNotForward()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        // No active call between them
        var hub = b.Build();

        await hub.SendSignal("SDP_OFFER", TargetId);

        // Target should NOT receive signal
        Assert.False(b.ClientProxies.ContainsKey(TargetId));
    }

    [Fact]
    public async Task SendSignal_WhenTargetNotInSameCall_DoesNotForward()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddUser("Charlie", "conn-charlie");
        b.AddCall(CallerId, "conn-charlie"); // Alice is in call with Charlie, not Bob
        var hub = b.Build();

        await hub.SendSignal("SDP_OFFER", TargetId);

        Assert.False(b.ClientProxies.ContainsKey(TargetId));
    }

    [Fact]
    public async Task SendSignal_WhenSenderNotInUserList_DoesNothing()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Bob", TargetId);
        b.AddCall(CallerId, TargetId); // call exists but Alice not in user list
        var hub = b.Build();

        await hub.SendSignal("SDP_OFFER", TargetId);

        Assert.False(b.ClientProxies.ContainsKey(TargetId));
    }

    // ────────────────────────────── OnDisconnectedAsync ─────────────────────

    [Fact]
    public async Task OnDisconnectedAsync_HangsUpAndRemovesUser()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        b.AddUser("Bob", TargetId);
        b.AddCall(CallerId, TargetId);
        var hub = b.Build();

        await hub.OnDisconnectedAsync(null);

        // User removed from list
        Assert.DoesNotContain(b.Users, u => u.ConnectionId == CallerId);

        // Call cleaned up
        Assert.Empty(b.UserCalls);

        // Peer notified
        var targetProxy = b.ClientProxies[TargetId];
        targetProxy.Verify(
            p => p.callEnded(
                It.IsAny<User>(),
                It.Is<string>(msg => msg.Contains("hung up"))),
            Times.Once);

        // User list broadcast
        b.AllProxy.Verify(p => p.updateUserList(b.Users), Times.AtLeastOnce);
    }

    [Fact]
    public async Task OnDisconnectedAsync_WhenNotInCall_StillRemovesUser()
    {
        var b = CreateHub(CallerId);
        b.AddUser("Alice", CallerId);
        var hub = b.Build();

        await hub.OnDisconnectedAsync(null);

        Assert.Empty(b.Users);
        b.AllProxy.Verify(p => p.updateUserList(b.Users), Times.AtLeastOnce);
    }
}
