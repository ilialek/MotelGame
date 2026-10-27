from pathlib import Path
import subprocess,time,datetime,sys
P=next((q for q in Path(__file__).resolve().parents if (q/'Assets/Game').is_dir()),Path('/Users/ilalekomtsev/MotelGame'));mode=sys.argv[1] if len(sys.argv)>1 else 'success';solo='--solo' in sys.argv
R=P/'Documentation/BaggageInspectionM15'/((mode+'-solo-' if solo else mode+'-')+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir(parents=True)
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';processes=[];late=set()
def start(role):
 cmd=[str(app),'-motel-baggage-test',role,'-motel-baggage-mode',mode,'-motel-baggage-results',str(R),'-logFile',str(R/(role+'.log'))]
 cmd+=['-screen-fullscreen','0','-screen-width','960','-screen-height','700'] if '--graphics' in sys.argv and not role.startswith('late') else ['-batchmode','-nographics']
 p=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started',role,p.pid,flush=True)
try:
 start('solo' if solo else 'host');time.sleep(3)
 if not solo:start('client')
 begin=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any((R/(role+'.txt')).exists() and (R/(role+'.txt')).read_text().startswith('FAIL') for role,p in processes):break
  for observer in ['late-inspection','late-hunt']:
   if (R/(observer+'.signal')).exists() and observer not in late and not solo:late.add(observer);start(observer)
  elapsed=time.monotonic()-begin
  if elapsed>1000:raise RuntimeError('Hunt test timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:
  f=R/(role+'.txt');print(role,p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
 assert all(p.poll()==0 and (R/(role+'.txt')).exists() and (R/(role+'.txt')).read_text().startswith('PASS') for role,p in processes), 'One or more test actors did not complete successfully'
finally:
 for _,p in processes:
  if p.poll() is None:
   p.terminate()
   try:p.wait(timeout=5)
   except subprocess.TimeoutExpired:p.kill();p.wait()
 print('Results:',R,flush=True)
