#include "MultipeerDelegate.h"

typedef void* ManagedMultipeerDelegate;
typedef void* ManagedNSError;
typedef void* ManagedPeerMessage;
typedef void* ManagedPeerInfo;

ManagedMultipeerDelegate UnityMC_Delegate_initWithPeerInfo(void* peerInfo, void* serviceType)
{
    MultipeerDelegate* delegate = [[MultipeerDelegate alloc] initWithPeerInfo:(__bridge PeerInfo*)peerInfo
                                                              serviceType:(__bridge NSString*)serviceType];
    return (__bridge_retained void*)delegate;
}

ManagedNSError UnityMC_Delegate_sendToAllPeers(void* self, void* nsdata, int length, int mode)
{
    NSData* data = (__bridge NSData*)nsdata;
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    NSError* error = [delegate sendToAllPeers:data withMode:(MCSessionSendDataMode)mode];
    return (__bridge_retained void*)error;
}

ManagedNSError UnityMC_Delegate_sendToPeer(void* self, void* peerID, void* nsdata, int length, int mode)
{
    NSUUID* userID = (__bridge NSUUID*)peerID);
    NSData* data = (__bridge NSData*)nsdata;
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    NSError* error = [delegate sendToPeerID:userID data:data withMode:(MCSessionSendDataMode)mode];
    return (__bridge_retained void*)error;
}

ManagedNSError UnityMC_Delegate_inviteDiscoveredPeer(void* self, void* peerID, void* invitation)
{
    NSUUID* userID = (__bridge NSUUID*)peerID);
    PeerInfo* inviationPeerInfo = (__bridge PeerInfo*)invitation);
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    NSError* error = [delegate inviteDiscoveredPeer:userID inviationPeerInfo];
    return (__bridge_retained void*)error;
}

void UnityMC_Delegate_rejectDiscoveredPeer(void* self, void* peerID)
{
    NSUUID* userID = (__bridge NSUUID*)peerID);
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    [delegate rejectDiscoveredPeer:userID];
}

ManagedNSError UnityMC_Delegate_acceptInvitationFrom(void* self, void* peerID)
{
    NSUUID* userID = (__bridge NSUUID*)peerID);
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    NSError* error = [delegate acceptInvitationFrom:userID];
    return (__bridge_retained void*)error;
}

void UnityMC_Delegate_rejectInvitationFrom(void* self, void* peerID)
{
    NSUUID* userID = (__bridge NSUUID*)peerID);
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    [delegate rejectInvitationFrom:userID];
}

int UnityMC_Delegate_receivedDataQueueSize(void* self)
{
    if (self == NULL)
        return 0;

    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (int)delegate.queueSize;
}

ManagedPeerMessage UnityMC_Delegate_dequeueReceivedData(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (__bridge_retained void*)delegate.dequeue;
}

int UnityMC_Delegate_errorCount(void* self)
{
    if (self == NULL)
        return 0;

    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (int)delegate.errorCount;
}

ManagedNSError UnityMC_Delegate_dequeueError(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (__bridge_retained void*)delegate.dequeueError;
}

int UnityMC_Delegate_discoveredQueueSize(void* self)
{
    if (self == NULL)
        return 0;

    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (int)delegate.discoveredQueueSize;
}

ManagedPeerInfo UnityMC_Delegate_dequeueDiscoveredPeer(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (__bridge_retained void*)delegate.dequeueDiscoveredPeer;
}

int UnityMC_Delegate_invitationQueueSize(void* self)
{
    if (self == NULL)
        return 0;

    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (int)delegate.invitationQueueSize;
}

ManagedPeerInfo UnityMC_Delegate_dequeueInvitation(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (__bridge_retained void*)delegate.dequeueInvitation;
}

int UnityMC_Delegate_connectedQueueSize(void* self)
{
    if (self == NULL)
        return 0;

    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (int)delegate.connectedQueueSize;
}

ManagedPeerInfo UnityMC_Delegate_dequeueConnectedPeer(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (__bridge_retained void*)delegate.dequeueConnectedPeer;
}

int UnityMC_Delegate_disconnectedQueueSize(void* self)
{
    if (self == NULL)
        return 0;

    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (int)delegate.disconnectedQueueSize;
}

ManagedPeerInfo UnityMC_Delegate_dequeueDisconnectedPeer(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (__bridge_retained void*)delegate.dequeueDisconnectedPeer;
}

int UnityMC_Delegate_connectedPeerCount(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return (int)delegate.connectedPeerCount;
}

void UnityMC_Delegate_setAdvertising(void* self, bool advertising)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    delegate.advertising = advertising;
}

bool UnityMC_Delegate_getAdvertising(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return delegate.advertising;
}

void UnityMC_Delegate_setBrowsing(void* self, bool browsing)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    delegate.browsing = browsing;
}

bool UnityMC_Delegate_getBrowsing(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    return delegate.browsing;
}

void UnityMC_Delegate_disconnect(void* self)
{
    MultipeerDelegate* delegate = (__bridge MultipeerDelegate*)self;
    delegate.disconnect;
}

int GetMaximumNumberOfPeers()
{
    return kMCSessionMaximumNumberOfPeers;
}

void UnityMC_CFRelease(void* ptr)
{
    if (ptr)
    {
        CFRelease(ptr);
    }
}
