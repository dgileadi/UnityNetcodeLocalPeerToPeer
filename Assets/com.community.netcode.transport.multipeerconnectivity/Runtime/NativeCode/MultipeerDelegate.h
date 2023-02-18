#import <MultipeerConnectivity/MultipeerConnectivity.h>
#include "PeerMessage.h"

@interface MultipeerDelegate : NSObject<MCSessionDelegate, MCNearbyServiceAdvertiserDelegate, MCNearbyServiceBrowserDelegate>

- (nullable instancetype)initWithPeerInfo:(nonnull PeerInfo *)peerInfo serviceType:(nonnull NSString *)serviceType;
- (nullable NSError*)sendToAllPeers:(nonnull NSData*)data withMode:(MCSessionSendDataMode)mode;
- (nullable NSError*)sendToPeerID:(nonnull NSString *)peerID data:(nonnull NSData*)data withMode:(MCSessionSendDataMode)mode;
- (NSUInteger)discoveredQueueSize;
- (nonnull PeerInfo*)dequeueDiscoveredPeer;
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
