/* Software API-contract driver, not Apple's AudioToolbox implementation. */
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stddef.h>
typedef struct{double rate;uint32_t id,flags,bpp,fpp,bpf,channels,bits,reserved;}ASBD;
typedef struct{uint32_t capacity;void*data;uint32_t size;void*user;uint32_t packetCapacity;void*packets;uint32_t packetCount;}Buffer;
typedef struct{double sample;uint64_t host;double rate;uint64_t word;int16_t subframes,divisor;uint32_t counter,type,smpteflags;int16_t hours,minutes,seconds,frames;uint32_t flags,reserved;}Stamp;
typedef void(*Callback)(void*,void*,void*);
typedef struct{Callback callback;void*context;Buffer*buffers[64];Buffer*owned[64];int count,ownedCount,playing;double frames;}Queue;
static int metrics[7],fail_enqueue;
static float captured[512];
_Static_assert(sizeof(ASBD)==40,"ASBD ABI");
_Static_assert(offsetof(Buffer,data)==8&&offsetof(Buffer,size)==16,"buffer ABI");
_Static_assert(sizeof(Stamp)==64,"timestamp ABI");
/* metrics: new queues, starts, pauses, stops, resets, allocations, live queues. */
int shim_metric(int index){return metrics[index];}
void shim_fail_next_enqueue(void){fail_enqueue=1;}
float shim_sample(int frame,int channel){return captured[frame*2+channel];}
int AudioQueueNewOutput(ASBD*f,Callback cb,void*ctx,void*loop,void*mode,uint32_t flags,void**out){
    (void)loop;(void)mode;(void)flags;
    if(f->rate!=44100||f->id!=0x6c70636d||f->flags!=9||f->bpf!=8||f->channels!=2||f->bits!=32||!cb)return -1;
    Queue*q=calloc(1,sizeof(Queue));q->callback=cb;q->context=ctx;*out=q;metrics[0]++;metrics[6]++;return 0;
}
int AudioQueueAllocateBuffer(Queue*q,uint32_t size,void**out){
    if(q->ownedCount>=64)return -6;
    Buffer*b=calloc(1,sizeof(Buffer));b->capacity=size;b->data=malloc(size);q->owned[q->ownedCount++]=b;*out=b;metrics[5]++;return 0;
}
int AudioQueueEnqueueBuffer(Queue*q,Buffer*b,uint32_t count,void*desc){
    if(fail_enqueue){fail_enqueue=0;return -9;}
    if(b->size>b->capacity||count||desc||q->count>=64)return -2;
    for(int i=0;i<q->count;i++)if(q->buffers[i]==b)return -7;
    q->buffers[q->count++]=b;return 0;
}
int AudioQueueStart(Queue*q,void*stamp){(void)stamp;q->playing=1;metrics[1]++;return 0;}
int AudioQueuePause(Queue*q){q->playing=0;metrics[2]++;return 0;}
int AudioQueueStop(Queue*q,uint8_t now){q->playing=0;q->frames=0;q->count=0;metrics[3]++;return now==1?0:-3;}
int AudioQueueReset(Queue*q){q->frames=0;q->count=0;metrics[4]++;return 0;}
int AudioQueueDispose(Queue*q,uint8_t now){
    if(now!=1)return -4;
    for(int i=0;i<q->ownedCount;i++){free(q->owned[i]->data);free(q->owned[i]);}
    metrics[6]--;free(q);return 0;
}
int AudioQueueGetCurrentTime(Queue*q,void*timeline,Stamp*s,uint8_t*disc){
    (void)timeline;memset(s,0,sizeof(*s));s->sample=q->frames;s->flags=1;*disc=0;return 0;
}
/* Buffer acquisition/refill and audible device time are deliberately independent. */
void shim_prefetch(void*p){
    Queue*q=p;if(!q->playing||!q->count)return;
    Buffer*b=q->buffers[0];memmove(q->buffers,q->buffers+1,(--q->count)*sizeof(Buffer*));
    memset(captured,0,sizeof(captured));memcpy(captured,b->data,b->size<sizeof(captured)?b->size:sizeof(captured));
    q->callback(q->context,q,b);
}
void shim_advance(void*p,int frames){Queue*q=p;if(q->playing)q->frames+=frames;}
