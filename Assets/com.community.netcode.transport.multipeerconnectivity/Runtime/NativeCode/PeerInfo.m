#import "PeerInfo.h"

@implementation PeerInfo

- (nullable instancetype)initWithPeerID:(nonnull NSString *)peerID displayName:(nonnull NSString *)displayName mode:(char)mode
{
    if (self = [super init])
    {
        self->_peerID = peerID;
        self->_displayName = displayName;
        self->_mode = mode;
    }

    return self;
}

- (id)initWithCoder:(NSCoder *)aDecoder
{
    if (self = [super init]) {
        self->_peerID = [aDecoder decodeObjectForKey:@"peerID"];
        self->_mode = [aDecoder decodeIntForKey:@"mode"];
    }
    return self;
}

- (void)encodeWithCoder:(NSCoder *)aCoder
{
    [aCoder encodeObject:self.peerID forKey:@"peerID"];
    [aCoder encodeInt:self.mode forKey:@"mode"];
}

- (id)initWithDiscoveryInfo:(NSDictionary<NSString *,NSString *> *)discoveryInfo displayName:(NSString *)displayName
{
    if (self = [super init]) {
        self->_peerID = [discoveryInfo objectForKey:@"i"];
        self->_displayName = displayName;
        self->_mode = (char)[[discoveryInfo objectForKey:@"m"] intValue];
    }
    return self;
}

- (NSDictionary<NSString *,NSString *> *_Nonnull)toDiscoveryInfo
{
    return @{ @"i": peerID, @"m": [@((int)mode) stringValue] };
}

@end
