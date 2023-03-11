#import "PeerInfo.h"

@implementation PeerInfo

- (nullable instancetype)initWithPeerID:(nonnull NSUUID *)peerID displayName:(nonnull NSString *)displayName startMode:(char)startMode platform:(char)platform
{
    if (self = [super init])
    {
        self->_peerID = peerID;
        self->_displayName = displayName;
        self->_startMode = startMode;
        self->_platform = platform;
        self->_mode = 0;
        self->_peerCount = 0;
        self->_serverPeerID = nil;
    }

    return self;
}

- (id)initWithCoder:(NSCoder *)aDecoder
{
    if (self = [super init]) {
        self->_peerID = [aDecoder decodeObjectForKey:@"peerID"];
        self->_displayName = nil;
        self->_startMode = [aDecoder decodeIntForKey:@"startMode"];
        self->_platform = [aDecoder decodeIntForKey:@"platform"];
        self->_mode = [aDecoder decodeIntForKey:@"mode"];
        self->_peerCount = [aDecoder decodeIntForKey:@"peerCount"];
        self->_serverPeerID = [aDecoder decodeObjectForKey:@"serverPeerID"];
    }
    return self;
}

- (void)encodeWithCoder:(NSCoder *)aCoder
{
    [aCoder encodeObject:self.peerID forKey:@"peerID"];
    [aCoder encodeInt:self.startMode forKey:@"startMode"];
    [aCoder encodeInt:self.platform forKey:@"platform"];
    [aCoder encodeInt:self.mode forKey:@"mode"];
    [aCoder encodeInt:self.peerCount forKey:@"peerCount"];
    [aCoder encodeObject:self.serverPeerID forKey:@"serverPeerID"];
}

- (id)initWithDiscoveryInfo:(NSDictionary<NSString *,NSString *> *)discoveryInfo displayName:(NSString *)displayName
{
    if (self = [super init]) {
        self->_peerID = [[NSUUID alloc] initWithUUIDString:[discoveryInfo objectForKey:@"i"]];
        self->_displayName = displayName;
        self->_startMode = (char)[[discoveryInfo objectForKey:@"m"] intValue];
        self->_platform = (char)[[discoveryInfo objectForKey:@"p"] intValue];
        self->_mode = 0;
        self->_peerCount = 0;
        self->_serverPeerID = nil;
    }
    return self;
}

- (NSDictionary<NSString *,NSString *> *_Nonnull)toDiscoveryInfo
{
    // only include fields that don't change, since discovery info can't really
    // be updated, see https://stackoverflow.com/questions/62029043/restarting-multipeerconnectivity-service-advertiser-with-different-discoveryinfo
    return @{ @"i": [peerID UUIDString], @"m": [@((int)startMode) stringValue], @"p": [@((int)platform) stringValue] };
}

@end
