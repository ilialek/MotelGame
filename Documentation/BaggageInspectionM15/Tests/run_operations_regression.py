from pathlib import Path
import subprocess,time,datetime,sys
P=next((q for q in Path(__file__).resolve().parents if (q/'Assets/Game').is_dir()),Path('/Users/ilalekomtsev/MotelGame'))
night='--night' in sys.argv; edges='--edges' in sys.argv; route='--door-route' in sys.argv
R=P/'Documentation/BaggageInspectionM15'/('regression-door-route-' if route else 'regression-edges-' if edges else 'regression-night-' if night else 'regression-focused-')
R=R.with_name(R.name+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir(parents=True)
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';processes=[];observed=set()
def start(role):
 cmd=[str(app),'-motel-operations-test',role,'-motel-operations-results',str(R),'-logFile',str(R/(role+'.log')),'-motel-online','-motel-profile',('night-' if night else 'regression-edges-' if edges else 'route-' if route else 'regression-focused-')+role]
 if route:cmd+=['-motel-operations-door-route']
 if night:cmd+=['-motel-operations-night']
 if edges:cmd+=['-motel-operations-edges']
 if '--graphics' in sys.argv and not role.startswith('observer'):cmd+=['-screen-fullscreen','0','-screen-width','800','-screen-height','600']
 else:cmd+=['-batchmode','-nographics']
 p=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started',role,p.pid,flush=True)
try:
 start('host');time.sleep(3);start('client');begin=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any(p.poll() not in [None,0] for _,p in processes):break
  for stage in ('bag','fault','relocation'):
   if stage not in observed and (R/('observe-'+stage+'.signal')).exists():observed.add(stage);start('observer-'+stage)
  elapsed=time.monotonic()-begin
  if elapsed>1500:raise RuntimeError('M11 integration timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:
  f=R/(role+'.txt');print(role,'exit',p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
 assert all(p.poll()==0 and (R/(role+'.txt')).read_text().startswith('PASS') for role,p in processes)
 assert night or edges or route or len(observed)==3
finally:
 for _,p in processes:
  if p.poll() is None:
   p.terminate()
   try:p.wait(timeout=5)
   except subprocess.TimeoutExpired:p.kill();p.wait()
 print('Results:',R,flush=True)
