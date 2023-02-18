#import <Foundation/Foundation.h>

@interface PeerInfo : NSObject, NSCoding

@property(strong, nonatomic, readonly) NSString *_Nonnull peerID;
@property(strong, nonatomic, readonly) NSString *_Nonnull peerDisplayName;
@property char mode;

- (nullable instancetype)initWithPeerID:(nonnull NSString *)peerID displayName:(nonnull NSString *)displayName mode:(char)mode;
- (id)initWithDiscoveryInfo:(NSDictionary<NSString *, NSString *> *)discoveryInfo displayName:(NSString *)displayName;
- (NSDictionary<NSString *, NSString *> *_Nonnull)toDiscoveryInfo;

@end
