#import <MultipeerConnectivity/MultipeerConnectivity.h>
#include "PeerMessage.h"

@interface MultipeerDelegate : NSObject<MCSessionDelegate, MCNearbyServiceAdvertiserDelegate, MCNearbyServiceBrowserDelegate>

- (nullable instancetype)initWithPeerInfo:(nonnull PeerInfo *)peerInfo serviceType:(nonnull NSString *)serviceType;
- (nullable NSError*)sendToAllPeers:(nonnull NSData*)data withMode:(MCSessionSendDataMode)mode;
- (nullable NSError*)sendToPeerID:(nonnull NSUUID *)peerID data:(nonnull NSData*)data withMode:(MCSessionSendDataMode)mode;
- (nullable NSError*)inviteDiscoveredPeer:(nonnull NSUUID *)peerID (nonnull PeerInfo *)invitation;
- (void)rejectDiscoveredPeer:(nonnull NSUUID *)peerID;
- (nullable NSError *)acceptInvitationFrom:(nonnull NSUUID *)peerID;
- (void)rejectInvitationFrom:(nonnull NSUUID *)peerID;
- (NSUInteger)discoveredQueueSize;
- (nonnull PeerInfo*)dequeueDiscoveredPeer;
- (NSUInteger)invitationQueueSize;
- (nonnull PeerInfo*)dequeueInvitation;
- (NSUInteger)connectedQueueSize;
- (nonnull PeerInfo*)dequeueConnectedPeer;
- (NSUInteger)disconnectedQueueSize;
- (nonnull PeerInfo*)dequeueDisconnectedPeer;
- (NSUInteger)connectedPeerCount;
- (NSUInteger)queueSize;
- (nonnull PeerMessage*)dequeue;
- (NSUInteger)errorCount;
- (nonnull NSError*)dequeueError;
- (void)disconnect;

@property BOOL advertising;
@property BOOL browsing;

@end
