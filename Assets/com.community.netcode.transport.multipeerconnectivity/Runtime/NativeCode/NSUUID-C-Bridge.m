#import <Foundation/Foundation.h>

void* UnityMC_NSUUID_createRandom()
{
    NSUUID* uuid = [[NSUUID alloc] init];
    return (__bridge_retained void*)uuid;
}

void* UnityMC_NSUUID_createWithBytes(void* bytes)
{
    NSUUID* uuid = [[NSUUID alloc] initWithUUIDBytes: bytes];
    return (__bridge_retained void*)uuid;
}

void* UnityMC_NSUUID_getBytes(void* self, void* buffer)
{
    NSUUID* uuid = (__bridge NSUUID*)self;
    [uuid getUUIDBytes:buffer];
}

void* UnityMC_NSUUID_serialize(void* self)
{
    NSUUID* uuid = (__bridge NSUUID*)self;
    uuid_t buffer;
    [uuid getUUIDBytes:buffer];
    NSData* data = [NSData dataWithBytes:buffer length:16];
    return (__bridge_retained void*)data;
}

void* UnityMC_NSUUID_deserialize(void* serializedUUID)
{
    NSData* data = (__bridge NSData*)serializedUUID;
    NSUUID* uuid = [[NSUUID alloc] initWithUUIDBytes: data.bytes];
    return (__bridge_retained void*)uuid;
}

NSComparisonResult UnityMC_NSUUID_compare(void* self, void* other)
{
    NSUUID* uuid = (__bridge NSUUID*)self;
    NSUUID* otherUUID = (__bridge NSUUID*)other;
    return [uuid compare:otherUUID];
}

NSUInteger UnityMC_NSUUID_hash(void* self)
{
    NSUUID* uuid = (__bridge NSUUID*)self;
    return [uuid hash];
}
