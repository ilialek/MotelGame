from pathlib import Path
import subprocess,time,datetime
P=Path(__file__).resolve().parents[3];R=P/'Documentation/GuestLifecycleM6'/('edge-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir()
app=P/'Builds/GuestLifecycleM6/MotelGame.app/Contents/MacOS/MotelGame';assert app.exists();processes=[]
try:
 for role in ['host','client']:
  p=subprocess.Popen([str(app),'-batchmode','-nographics','-motel-lifecycle-test',role,'-motel-lifecycle-edge','-motel-lifecycle-results',str(R),'-logFile',str(R/(role+'.log'))],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started edge',role,p.pid,flush=True)
  if role=='host':time.sleep(3)
 start=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any(p.poll() not in [None,0] for _,p in processes):break
  elapsed=time.monotonic()-start
  if elapsed>480:raise RuntimeError('Edge timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:print(role,p.poll(),(R/(role+'.txt')).read_text() if (R/(role+'.txt')).exists() else 'No report',flush=True)
 assert all(p.poll()==0 for _,p in processes)
finally:
 for _,p in processes:
  if p.poll() is None:p.terminate()
 print('Results:',R,flush=True)
