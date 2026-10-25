from pathlib import Path
import subprocess,time,datetime,sys
P=Path(__file__).resolve().parents[3]
R=P/'Documentation/NightShiftM10'/('run-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir()
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';processes=[];observed=False
seconds=sys.argv[1] if len(sys.argv)>1 else '900'
def start(role):
 cmd=[str(app),'-motel-night-test',role,'-motel-night-results',str(R),'-motel-night-seconds',seconds,'-logFile',str(R/(role+'.log'))]
 if '--graphics' in sys.argv and role!='observer':cmd += ['-screen-fullscreen','0','-screen-width','800','-screen-height','600']
 else:cmd += ['-batchmode','-nographics']
 p=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started',role,p.pid,flush=True);return p
try:
 start('host');time.sleep(3);start('client');begin=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any(p.poll() not in [None,0] for _,p in processes):break
  if not observed and (R/'observe-repair.signal').exists():observed=True;start('observer')
  elapsed=time.monotonic()-begin
  if elapsed>float(seconds)+600:raise RuntimeError('Night integration timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:
  f=R/(role+'.txt');print(role,'exit',p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
 assert observed and all(p.poll()==0 for _,p in processes)
 assert all((R/(role+'.txt')).exists() and (R/(role+'.txt')).read_text().startswith('PASS '+role) for role,_ in processes)
finally:
 for _,p in processes:
  if p.poll() is None:p.terminate()
 print('Results:',R,flush=True)
