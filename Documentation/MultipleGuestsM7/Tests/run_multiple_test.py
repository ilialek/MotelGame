from pathlib import Path
import subprocess,time,datetime
P=Path(__file__).resolve().parents[3];R=P/'Documentation/MultipleGuestsM7'/('run-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir()
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';assert app.exists();processes=[];observers=set()
def start(role):
 cmd=[str(app),'-batchmode','-nographics','-motel-multiple-test',role,'-motel-multiple-results',str(R),'-logFile',str(R/(role+'.log'))]
 proc=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,proc));print('Started',role,proc.pid,flush=True);return proc
try:
 host=start('host');time.sleep(3);client=start('client');begin=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any(p.poll() not in [None,0] for _,p in processes):break
  for phase in ['observer']:
   if phase not in observers and (R/'observe.signal').exists():observers.add(phase);start(phase)
  elapsed=time.monotonic()-begin
  if elapsed>1400:raise RuntimeError('M7 integration timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:
  f=R/(role+'.txt');print(role,'exit',p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
 assert len(observers)==1 and all(p.poll()==0 for _,p in processes)
finally:
 for _,p in processes:
  if p.poll() is None:p.terminate()
 print('Results:',R,flush=True)
