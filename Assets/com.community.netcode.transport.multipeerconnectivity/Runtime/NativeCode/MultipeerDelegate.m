#import "MultipeerDelegate.h"

@implementation MultipeerDelegate

MCSession* m_Session;
MCPeerID* m_PeerID;
PeerInfo* m_PeerInfo;
NSMutableDictionary *m_PeerInfoByID;
NSMutableDictionary *m_HandlerByID;
NSMutableArray* m_DiscoveredQueue;
NSMutableArray* m_InvitationQueue;
NSMutableArray* m_ConnectedQueue;
NSMutableArray* m_DisconnectedQueue;
NSMutableArray* m_Queue;
NSMutableArray* m_ErrorQueue;
MCNearbyServiceAdvertiser* m_ServiceAdvertiser;
MCNearbyServiceBrowser* m_ServiceBrowser;
BOOL m_Advertising;
BOOL m_Browsing;

- (instancetype)initWithPeerInfo:(nonnull PeerInfo *)peerInfo serviceType:(nonnull NSString *)serviceType
{
    if (self = [super init])
    {
        m_Advertising = false;
        m_Browsing = false;
        m_Queue = [[NSMutableArray alloc] init];
        m_DiscoveredQueue = [[NSMutableArray alloc] init];
        m_InvitationQueue = [[NSMutableArray alloc] init];
        m_ConnectedQueue = [[NSMutableArray alloc] init];
        m_DisconnectedQueue = [[NSMutableArray alloc] init];
        m_ErrorQueue = [[NSMutableArray alloc] init];
        m_PeerInfoByID = [[NSMutableDictionary alloc] init];
        m_HandlerByID = [[NSMutableDictionary alloc] init];
        m_PeerID = [[MCPeerID alloc] initWithDisplayName: peerInfo.displayName];
        m_PeerInfo = peerInfo;
        m_Session = [[MCSession alloc] initWithPeer:m_PeerID
                                   securityIdentity:nil
                               encryptionPreference:MCEncryptionRequired];
        m_Session.delegate = self;

        NSDictionary *discoveryInfo = [peerInfo toDiscoveryInfo];

        m_ServiceAdvertiser = [[MCNearbyServiceAdvertiser alloc] initWithPeer:m_PeerID
                                                                discoveryInfo:discoveryInfo
                                                                  serviceType:serviceType];
        m_ServiceAdvertiser.delegate = self;

        m_ServiceBrowser = [[MCNearbyServiceBrowser alloc] initWithPeer:m_PeerID
                                                            serviceType:serviceType];
        m_ServiceBrowser.delegate = self;
    }

    return self;
}

- (BOOL)advertising
{
    return m_Advertising;
}

- (void)setAdvertising:(BOOL)advertising
{
    if (m_Advertising == advertising)
        return;

    if (advertising)
    {
        [m_ServiceAdvertiser startAdvertisingPeer];
    }
    else
    {
        [m_ServiceAdvertiser stopAdvertisingPeer];
    }

    m_Advertising = advertising;
}

- (BOOL)browsing
{
    return m_Browsing;
}

- (void)setBrowsing:(BOOL)browsing
{
    if (m_Browsing == browsing)
        return;

    if (browsing)
    {
        [m_ServiceBrowser startBrowsingForPeers];
    }
    else
    {
        [m_ServiceBrowser stopBrowsingForPeers];
    }

    m_Browsing = browsing;
}

- (NSError*)sendToAllPeers:(nonnull NSData*)data withMode:(MCSessionSendDataMode)mode
{
    if (m_Session.connectedPeers.count == 0)
        return nil;

    NSError* error = nil;
    [m_Session sendData:data
                toPeers:m_Session.connectedPeers
               withMode:mode
                  error:&error];

    return error;
}

- (MCPeerID *)findMCPeerID(nonnull NSUUID *)peerID
{
    @synchronized (m_PeerInfoByID)
    {
        for (MCPeerID *key in m_PeerInfoByID)
            if ([peerID isEqual(m_PeerInfoByID[key].peerID))
                return key;
    }
    return nil;
}

- (NSError*)sendToPeerID(nonnull NSUUID*)peerID data:(nonnull NSData*)data withMode:(MCSessionSendDataMode)mode
{
    if (m_Session.connectedPeers.count == 0)
        return nil;

    MCPeerID* peer = [findMCPeerID peerID];
    if (peer == nil)
        return nil;

    NSError* error = nil;
    [m_Session sendData:data
                toPeers:@[peer]
               withMode:mode
                  error:&error];

    return error;
}

- (NSUInteger)queueSize
{
    @synchronized (m_Queue)
    {
        return m_Queue.count;
    }
}

- (nonnull PeerMessage*)dequeue
{
    @synchronized (m_Queue)
    {
        PeerMessage* data = [m_Queue objectAtIndex:0];
        [m_Queue removeObjectAtIndex:0];
        return data;
    }
}

- (NSUInteger)errorCount
{
    @synchronized (m_ErrorQueue)
    {
        return m_ErrorQueue.count;
    }
}

- (nonnull NSError*)dequeueError
{
    @synchronized (m_ErrorQueue)
    {
        NSError* error = [m_ErrorQueue objectAtIndex:0];
        [m_ErrorQueue removeObjectAtIndex:0];
        return error;
    }
}

- (NSUInteger)discoveredQueueSize
{
    @synchronized (m_DiscoveredQueue)
    {
        return m_DiscoveredQueue.count;
    }
}

- (nonnull PeerInfo*)dequeueDiscoveredPeer
{
    @synchronized (m_DiscoveredQueue)
    {
        PeerInfo* peerInfo = [m_DiscoveredQueue objectAtIndex:0];
        [m_DiscoveredQueue removeObjectAtIndex:0];
        return peerInfo;
    }
}

- (NSUInteger)invitationQueueSize
{
    @synchronized (m_InvitationQueue)
    {
        return m_InvitationQueue.count;
    }
}

- (nonnull PeerInfo*)dequeueInvitation
{
    @synchronized (m_InvitationQueue)
    {
        PeerInfo* peerInfo = [m_InvitationQueue objectAtIndex:0];
        [m_InvitationQueue removeObjectAtIndex:0];
        return peerInfo;
    }
}

- (NSUInteger)connectedQueueSize
{
    @synchronized (m_ConnectedQueue)
    {
        return m_ConnectedQueue.count;
    }
}

- (nonnull PeerInfo*)dequeueConnectedPeer
{
    @synchronized (m_ConnectedQueue)
    {
        PeerInfo* peerInfo = [m_ConnectedQueue objectAtIndex:0];
        [m_ConnectedQueue removeObjectAtIndex:0];
        return peerInfo;
    }
}

- (NSUInteger)disconnectedQueueSize
{
    @synchronized (m_DisconnectedQueue)
    {
        return m_DisconnectedQueue.count;
    }
}

- (nonnull PeerInfo*)dequeueDisconnectedPeer
{
    @synchronized (m_DisconnectedQueue)
    {
        PeerInfo* peerInfo = [m_DisconnectedQueue objectAtIndex:0];
        [m_DisconnectedQueue removeObjectAtIndex:0];
        return peerInfo;
    }
}

- (NSUInteger)connectedPeerCount
{
    return m_Session.connectedPeers.count;
}

- (void)disconnect
{
    @synchronized (m_PeerInfoByID)
    {
        [m_PeerInfoByID removeAllObjects];
    }
    @synchronized (m_DiscoveredQueue)
    {
        [m_DiscoveredQueue removeAllObjects];
    }
    @synchronized (m_InvitationQueue)
    {
        [m_InvitationQueue removeAllObjects];
    }
    @synchronized (m_ConnectedQueue)
    {
        [m_ConnectedQueue removeAllObjects];
    }
    @synchronized (m_DisconnectedQueue)
    {
        [m_DisconnectedQueue removeAllObjects];
    }
    @synchronized (m_Queue)
    {
        [m_Queue removeAllObjects];
    }
    @synchronized (m_ErrorQueue)
    {
        [m_ErrorQueue removeAllObjects];
    }
    [m_Session disconnect];
}

- (void)session:(nonnull MCSession *)session didFinishReceivingResourceWithName:(nonnull NSString *)resourceName fromPeer:(nonnull MCPeerID *)mcPeerID atURL:(nullable NSURL *)localURL withError:(nullable NSError *)error {
    // Not used.
}

- (void)session:(nonnull MCSession *)session didReceiveData:(nonnull NSData *)data fromPeer:(nonnull MCPeerID *)mcPeerID
{
    PeerInfo *peer;
    @synchronized (m_PeerInfoByID)
    {
        peer = m_PeerInfoByID[mcPeerID];
    }
    PeerMessage *peerMessage = [[PeerMessage alloc] initWithPeerID:peer.peerID data:data];

    @synchronized (m_Queue)
    {
        [m_Queue addObject:peerMessage];
    }
}

- (void)session:(nonnull MCSession *)session didReceiveStream:(nonnull NSInputStream *)stream withName:(nonnull NSString *)streamName fromPeer:(nonnull MCPeerID *)mcPeerID
{
    // Not used.
}

- (void) session:(MCSession*)session didReceiveCertificate:(NSArray*)certificate fromPeer:(MCPeerID*)mcPeerID certificateHandler:(void (^)(BOOL accept))certificateHandler
{
    if (certificateHandler != nil) { certificateHandler(YES); }
}

- (void)session:(nonnull MCSession *)session didStartReceivingResourceWithName:(nonnull NSString *)resourceName fromPeer:(nonnull MCPeerID *)mcPeerID withProgress:(nonnull NSProgress *)progress
{
    // Not used.
}

- (void)session:(nonnull MCSession *)session peer:(nonnull MCPeerID *)mcPeerID didChangeState:(MCSessionState)state
{
    PeerInfo *peerInfo;
    @synchronized (m_PeerInfoByID)
    {
        peerInfo = m_PeerInfoByID[mcPeerID];
    }
    if (!peerInfo) { return; }

    if (state == MCSessionStateConnected)
    {
        @synchronized (m_ConnectedQueue)
        {
            [m_ConnectedQueue addObject:peerInfo];
        }
    }
    else if (state == MCSessionStateNotConnected)
    {
        @synchronized (m_PeerInfoByID)
        {
            [m_PeerInfoByID removeObjectForKey:mcPeerID];

            // if the same peer has already reconnected, don't fire
            for (MCPeerID* key in m_PeerInfoByID) {
                if ([peerInfo.peerID isEqual:m_PeerInfoByID[key].peerID])
                    return;
            }
        }

        @synchronized (m_DisconnectedQueue)
        {
            [m_DisconnectedQueue addObject:peerInfo];
        }
    }
}

- (void)advertiser:(nonnull MCNearbyServiceAdvertiser *)advertiser didReceiveInvitationFromPeer:(nonnull MCPeerID *)mcPeerID withContext:(nullable NSData *)context invitationHandler:(nonnull void (^)(BOOL, MCSession * _Nullable))invitationHandler
{
    PeerInfo *peerInfo = [NSKeyedUnarchiver unarchivedObjectOfClass:[PeerInfo class] fromData:context error:nil];
    peerInfo.displayName = mcPeerID.displayName;

    @synchronized (m_PeerInfoByID)
    {
        if (![m_PeerInfoByID objectForKey:mcPeerID])
            [m_PeerInfoByID setObject:peerInfo forKey:mcPeerID];
    }

    @synchronized (m_HandlerByID)
    {
        [m_HandlerByID setObject:invitationHandler forKey:peerInfo.peerID];
    }

    @synchronized (m_InvitationQueue)
    {
        [m_InvitationQueue addObject:peerInfo];
    }
}

- (void)advertiser:(MCNearbyServiceAdvertiser *)advertiser didNotStartAdvertisingPeer:(NSError *)error
{
    NSLog(@"Unable to advertise peer: %@", error);
    @synchronized (m_ErrorQueue)
    {
        [m_ErrorQueue addObject:error];
    }
}

- (void)browser:(nonnull MCNearbyServiceBrowser *)browser foundPeer:(nonnull MCPeerID *)mcPeerID withDiscoveryInfo:(nullable NSDictionary<NSString *,NSString *> *)info
{
    if ([m_Session.connectedPeers containsObject:mcPeerID])
        return;

    PeerInfo *peerInfo = [[PeerInfo alloc] initWithDiscoveryInfo:info displayName:mcPeerID.displayName];

    @synchronized (m_PeerInfoByID)
    {
        if (![m_PeerInfoByID objectForKey:mcPeerID])
            [m_PeerInfoByID setObject:peerInfo forKey:mcPeerID];
    }

    @synchronized (m_DiscoveredQueue)
    {
        [m_DiscoveredQueue addObject:peerInfo];
    }
}

- (NSError*)inviteDiscoveredPeer:(nonnull NSUUID *)peerID (nonnull PeerInfo *)invitation
{
    MCPeerID *mcPeerID = [findMCPeerID peerID];
    if (mcPeerID == nil)
    {
        NSString *description = [NSString stringWithFormat:@"Unable to find a MCPeerID for peer ID %@", peerID];
        NSDictionary *userInfo = @{ NSLocalizedDescriptionKey : description };
        return [NSError errorWithDomain:@"Netcode.Transports.MultipeerConnectivity.ErrorDomain" code:-404 userInfo:nil];
    }

    NSData *context = [NSKeyedArchiver archivedDataWithRootObject:invitation requiringSecureCoding:FALSE error:nil];

    // Invite the peer to join our session
    [browser invitePeer:mcPeerID
              toSession:m_Session
            withContext:context
                timeout:10];

    return nil;
}

- (void)rejectDiscoveredPeer:(nonnull NSUUID *)peerID
{
    @synchronized (m_PeerInfoByID)
    {
        for (MCPeerID *key in m_PeerInfoByID)
            if ([peerID isEqual(m_PeerInfoByID[key].peerID))
                [m_PeerInfoByID removeObjectForKey:key];
    }
}

- (nullable NSError *)acceptInvitationFrom:(nonnull NSUUID *)peerID
{
    void (^invitationHandler)(BOOL, MCSession * _Nullable);
    @synchronized (m_HandlerByID)
    {
        invitationHandler = [m_HandlerByID objectForKey:peerID];
        [m_HandlerByID removeObjectForKey:peerID];
    }

    if (invitationHandler == nil)
    {
        NSString *description = [NSString stringWithFormat:@"Unable to find an invitation handler for peer ID %@", peerID];
        NSDictionary *userInfo = @{ NSLocalizedDescriptionKey : description };
        return [NSError errorWithDomain:@"Netcode.Transports.MultipeerConnectivity.ErrorDomain" code:-405 userInfo:nil];
    }

    invitationHandler(TRUE, m_Session);
}

- (void)rejectInvitationFrom:(nonnull NSUUID *)peerID
{
    void (^invitationHandler)(BOOL, MCSession * _Nullable);
    @synchronized (m_HandlerByID)
    {
        invitationHandler = [m_HandlerByID objectForKey:peerID];
        [m_HandlerByID removeObjectForKey:peerID];
    }

    if (invitationHandler != nil)
        invitationHandler(FALSE, m_Session);

    @synchronized (m_PeerInfoByID)
    {
        for (MCPeerID *key in m_PeerInfoByID)
            if ([peerID isEqual(m_PeerInfoByID[key].peerID))
                [m_PeerInfoByID removeObjectForKey:key];
    }
}

- (void)browser:(nonnull MCNearbyServiceBrowser *)browser lostPeer:(nonnull MCPeerID *)mcPeerID
{
    @synchronized (m_PeerInfoByID)
    {
        [m_PeerInfoByID removeObjectForKey:mcPeerID];
    }
}

- (void)browser:(MCNearbyServiceBrowser *)browser didNotStartBrowsingForPeers:(NSError *)error
{
    NSLog(@"Unable to browse for peers: %@", error);
    @synchronized (m_ErrorQueue)
    {
        [m_ErrorQueue addObject:error];
    }
}

@end


char* ToCString(const NSString* nsString)
{
    if (nsString == NULL)
        return NULL;

    const char* nsStringUtf8 = [nsString UTF8String];
    //create a null terminated C string on the heap so that our string's memory isn't wiped out right after method's return
    char* cString = (char*)malloc(strlen(nsStringUtf8) + 1);
    strcpy(cString, nsStringUtf8);

    return cString;
}
