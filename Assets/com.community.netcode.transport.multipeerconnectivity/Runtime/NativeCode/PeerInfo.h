#import <Foundation/Foundation.h>

@interface PeerInfo : NSObject, NSCoding

@property(strong, nonatomic, readonly) NSUUID *_Nonnull peerID;
@property(strong, nonatomic, readonly) NSString *_Nonnull peerDisplayName;
@property char startMode;
@property char platform;
@property char mode;
@property char peerCount;
@property(strong, nonatomic) NSUUID * serverPeerID;

- (nullable instancetype)initWithPeerID:(nonnull NSUUID *)peerID displayName:(nonnull NSString *)displayName startMode:(char)startMode platform:(char)platform;
- (id)initWithDiscoveryInfo:(NSDictionary<NSString *, NSString *> *)discoveryInfo displayName:(NSString *)displayName;
- (NSDictionary<NSString *, NSString *> *_Nonnull)toDiscoveryInfo;

@end
