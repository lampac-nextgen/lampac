#!/usr/bin/env python3
"""Exercise native reader/cue decisions with deterministic cache and pipeline doubles.

Requires Python 3 and .NET 10. No media, GStreamer native libraries or test project.
This is not a pipeline integration test: only the decision methods are extracted.
"""
from pathlib import Path
import argparse
import subprocess
import tempfile

parser = argparse.ArgumentParser(description='Extract native Lampac decision methods into a deterministic C# IO-double regression harness.')
parser.add_argument('--source-dir', type=Path, default=Path(__file__).resolve().parents[1] / 'Services', help='GStreamer Services directory')
parser.add_argument('--producer', type=Path, help='Override Producer source, e.g. original baseline')
args = parser.parse_args()
temporary = tempfile.TemporaryDirectory(prefix='lampac-reader-')
root = Path(temporary.name)
src = args.source_dir
def extract(s,signature):
 start=s.index(signature);brace=s.index('{',start);depth=1;i=brace+1
 while depth:
  if s[i]=='{':depth+=1
  elif s[i]=='}':depth-=1
  i+=1
 return s[start:i]
p=(args.producer or src/'GStask.Producer.cs').read_text(encoding='utf-8-sig')
m=(src/'Mp4BoxReader.cs').read_text()
methods='\n'.join(extract(p,s) for s in ['public bool EnsureClientSegment(', 'bool CanReadForwardWithoutSeek('])
reader=extract(m,'int SelectCueVideoCount(')
# Only helpers/data containers are doubles; the two decision methods above and
# actual cue-selection comparison are extracted unchanged from the snapshot.
program='''using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
class Program {
 static int failures;
 static void Check(string name, Action test) {try {test();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
 static Harness New(int cursor,params int[] cached){return new Harness(cursor,cached);}
 static void Read(Harness h,int index){Require(h.EnsureClientSegment(index,default),"returned false");Require(h.Outputs[index]==index,$"content from {h.Outputs[index]} mislabeled {index}");Require(h.readerSegmentIndex==index,"reader index incorrect");}
 static int Main(){
  Check("normal adjacent",()=>Read(New(241),242));
  Check("normal forward gap",()=>{var h=New(241);Read(h,243);Require(h.SeekCount==0,"unneeded seek");});
  Check("cache/seek sequence 242 -> 241 -> 243",()=>{var h=New(-1);Read(h,242);Read(h,241);Read(h,243);});
  Check("cache/seek sequence 253 -> 252 -> 254",()=>{var h=New(-1);Read(h,253);Read(h,252);Read(h,254);});
  Check("cached gap fallback seek failure",()=>{var h=New(241,242);h.FailSeek=true;Require(!h.EnsureClientSegment(243,default),"seek failure ignored");Require(h.Outputs.Count==0,"consumed after failed seek");});
  Check("cached next before missing requested",()=>Read(New(241,242),243));
  Check("two cached intermediates",()=>Read(New(241,242,243),244));
  Check("cached middle after a real read",()=>Read(New(240,242),243));
  Check("cache plus equal duration cannot silently mislabel",()=>{var h=New(241,242);h.EqualDurations=true;Read(h,243);});
  Check("target cache hit leaves physical reader alone",()=>{var h=New(241,243);Require(h.EnsureClientSegment(243,default),"cache not served");Require(h.readerSegmentIndex==241,"cache advanced reader");Require(h.SeekCount==0,"unneeded seek");});
  Check("cache hit then cache hole",()=>{var h=New(240,241,242);Require(h.EnsureClientSegment(242,default),"cache not served");Read(h,243);});
  Check("backward missing seeks",()=>{var h=New(250);Read(h,243);Require(h.SeekCount==1,"missing seek");});
  Check("distant forward seeks",()=>{var h=New(230);Read(h,243);Require(h.SeekCount==1,"missing seek");});
  Check("failed seek returns false without reading",()=>{var h=New(250);h.FailSeek=true;Require(!h.EnsureClientSegment(243,default),"seek failure ignored");Require(h.Outputs.Count==0,"read after seek failure");});
  Check("cancellation prevents forward read",()=>{var h=New(241);using var c=new CancellationTokenSource();c.Cancel();Require(!h.EnsureClientSegment(243,c.Token),"cancel ignored");Require(h.Outputs.Count==0,"read after cancellation");});
  Check("cancellation during cached intermediate avoids seek",()=>{var h=New(241,242);using var c=new CancellationTokenSource();h.CancelOnCacheHit=c;Require(!h.EnsureClientSegment(243,c.Token),"cancel ignored");Require(h.SeekCount==0,"seek after cancellation");Require(h.Outputs.Count==0,"read after cancellation");});
  Check("invalid and ended requests",()=>{var h=New(241);Require(!h.EnsureClientSegment(-1,default),"negative index accepted");Require(!h.EnsureClientSegment(300,default),"past end accepted");});
  return failures==0?0:1;
 }
}
class Timeline {public int Count=300;}
class Config {public int segment_buffer=6;public int segment_seconds=6;}
class Harness {
 public int readerSegmentIndex; int physicalNext; int lastClientSegmentIndex;
 public bool IsDead=false,FailSeek=false,EqualDurations=false;public int SeekCount;
 public CancellationTokenSource? CancelOnCacheHit;
 public readonly Dictionary<int,int> Outputs=new();readonly HashSet<int> cache;
 readonly Timeline cueTimeline=new();readonly Config conf=new();
 public Harness(int cursor,int[] cached){readerSegmentIndex=cursor;physicalNext=cursor+1;cache=new(cached);}
 bool SegmentFileReady(int i)=>cache.Contains(i);
 bool SeekSegment(int i){SeekCount++;if(FailSeek)return false;physicalNext=i;return true;}
 ulong Duration(int i)=>EqualDurations?10386000000UL:i switch{242=>10427000000UL,243=>10385000000UL,253=>1918000000UL,_=>10386000000UL};
 bool EnsureSegment(int i,CancellationToken ct){
  if(SegmentFileReady(i)){CancelOnCacheHit?.Cancel();return true;} // live cache fast path; deterministic cancellation at that boundary
  if(ct.IsCancellationRequested)return false;
  var reader=new CueSelector(new[]{Duration(physicalNext),Duration(physicalNext+1),Duration(physicalNext+2)},Duration(i));
  int consumed=reader.Run();if(consumed==0)return false;
  Outputs[i]=physicalNext;physicalNext+=consumed;cache.Add(i);readerSegmentIndex=i;return true;
 }
'''+methods+'''
}
class CueSelector {
 bool _hasTargetSegment=true;List<Fragment> _video;ulong _targetSegmentStartNs=0,_targetSegmentEndNs,_targetToleranceNs=1000000;
 Track _videoTrack=new();
 public CueSelector(ulong[] durations,ulong expected){_video=new(){new(0)};long pts=0;foreach(var d in durations){pts+=checked((long)d);_video.Add(new(pts));}_targetSegmentEndNs=expected;}
 public int Run()=>SelectCueVideoCount();
 class Track{public uint Timescale=1000000000;}
 class Fragment{public bool StartsWithSync=true;public long Pts;public Fragment(long pts){Pts=pts;}}
 static long FirstPresentationTime(Fragment f)=>f.Pts;
 static ulong ToNanoseconds(ulong v,uint timescale)=>v;
'''+reader+'''\n}\n'''
(root/'reader-regression.cs').write_text(program)
try:
    result = subprocess.run(['dotnet', 'run', '--file', str(root / 'reader-regression.cs')], check=False)
finally:
    temporary.cleanup()
raise SystemExit(result.returncode)
