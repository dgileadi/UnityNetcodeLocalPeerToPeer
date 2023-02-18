#import <Foundation/Foundation.h>

@interface PeerMessage : NSObject

@property(strong, nonatomic, readonly) NSString *_Nonnull peerID;
@property(strong, nonatomic, readonly) NSData *_Nonnull data;

- (nullable instancetype)initWithPeerID:(nonnull NSString *)peerID data:(nonnull NSData *)data;

@end
