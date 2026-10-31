from pathlib import Path
import subprocess,time,datetime,sys
P=next((q for q in Path(__file__).resolve().parents if (q/'Assets/Game').is_dir()),Path('/Users/ilalekomtsev/MotelGame'));mode=sys.argv[1] if len(sys.argv)>1 else 'routes'
R=P/'Documentation/VentEntrances/Tests'/(('relay-' if '--relay' in sys.argv else '')+mode+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir(parents=True)
app=P/('Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame' if '--stable' in sys.argv else 'Builds/UndergroundCandidate/MotelGame.app/Contents/MacOS/MotelGame')
processes=[];late=False
def start(role):
 args=[str(app),'-motel-layout-test',role,'-motel-layout-mode',mode,'-motel-layout-results',str(R),'-logFile',str(R/(role+'.log'))]
 args+=['-screen-fullscreen','0','-screen-width','800','-screen-height','600'] if '--graphics' in sys.argv and role!='late' else ['-batchmode','-nographics']
 if '--relay' in sys.argv:args+=['-motel-online','-motel-profile',{'host':'layout16-host','client':'layout16-client1','late':'layout16-client2'}[role]]
 p=subprocess.Popen(args,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started',role,p.pid,flush=True)
try:
 start('host')
 if mode!='solo':
  time.sleep(3);start('client')
  if mode=='inspection':time.sleep(1);start('registrar')
 begin=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any((R/(role+'.txt')).exists() and (R/(role+'.txt')).read_text().startswith('FAIL') for role,p in processes):break
  if (R/'late.signal').exists() and not late:late=True;start('late')
  elapsed=time.monotonic()-begin
  if elapsed>2400:raise RuntimeError('Test timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:
  f=R/(role+'.txt');print(role,p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
 assert all(p.poll()==0 and (R/(role+'.txt')).exists() and (R/(role+'.txt')).read_text().startswith('PASS') for role,p in processes),'Not all actors passed'
finally:
 for role,p in processes:
  if p.poll() is None:
   p.terminate()
   try:p.wait(timeout=5)
   except subprocess.TimeoutExpired:p.kill();p.wait()
 print('Results:',R,flush=True)
