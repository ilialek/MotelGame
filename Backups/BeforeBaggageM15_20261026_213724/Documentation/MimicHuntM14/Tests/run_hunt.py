from pathlib import Path
import subprocess,time,datetime,sys
P=next((q for q in Path(__file__).resolve().parents if (q/'Assets/Game').is_dir()),Path('/Users/ilalekomtsev/MotelGame'));mode=sys.argv[1] if len(sys.argv)>1 else 'success';solo='--solo' in sys.argv
R=P/'Documentation/MimicHuntM14'/((mode+'-solo-' if solo else mode+'-')+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir(parents=True)
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';processes=[];late=False
def start(role):
 cmd=[str(app),'-motel-mimic-test',role,'-motel-mimic-mode',mode,'-motel-mimic-results',str(R),'-logFile',str(R/(role+'.log'))]
 cmd+=['-screen-fullscreen','0','-screen-width','960','-screen-height','700'] if '--graphics' in sys.argv and role!='late' else ['-batchmode','-nographics']
 p=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started',role,p.pid,flush=True)
try:
 start('solo' if solo else 'host');time.sleep(3)
 if not solo:start('client')
 begin=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any((R/(role+'.txt')).exists() and (R/(role+'.txt')).read_text().startswith('FAIL') for role,p in processes):break
  if (R/'late.signal').exists() and not late and not solo:late=True;start('late')
  elapsed=time.monotonic()-begin
  if elapsed>650:raise RuntimeError('Hunt test timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:
  f=R/(role+'.txt');print(role,p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
 assert all(p.poll()==0 and (R/(role+'.txt')).read_text().startswith('PASS') for role,p in processes)
finally:
 for _,p in processes:
  if p.poll() is None:
   p.terminate()
   try:p.wait(timeout=5)
   except subprocess.TimeoutExpired:p.kill();p.wait()
 print('Results:',R,flush=True)
