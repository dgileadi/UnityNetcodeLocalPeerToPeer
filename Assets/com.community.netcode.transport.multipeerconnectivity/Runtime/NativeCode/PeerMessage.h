#import <Foundation/Foundation.h>

@interface PeerMessage : NSObject

@property(strong, nonatomic, readonly) NSUUID *_Nonnull peerID;
@property(strong, nonatomic, readonly) NSData *_Nonnull data;

- (nullable instancetype)initWithPeerID:(nonnull NSUUID *)peerID data:(nonnull NSData *)data;

@end
