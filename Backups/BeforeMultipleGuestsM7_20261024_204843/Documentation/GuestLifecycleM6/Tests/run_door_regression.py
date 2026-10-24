from pathlib import Path
import subprocess,time,datetime
P=Path(__file__).resolve().parents[3];R=P/'Documentation/GuestLifecycleM6'/('door-regression-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir()
app=P/'Builds/GuestLifecycleM6/MotelGame.app/Contents/MacOS/MotelGame'
assert app.exists();processes=[]
def start(role):
 cmd=[str(app),'-batchmode','-nographics','-motel-door-test',role,'-motel-door-results',str(R),'-logFile',str(R/(role+'.log'))]
 p=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started',role,p.pid,flush=True);return p
try:
 host=start('host');time.sleep(3);client=start('client');begin=time.monotonic();late=None
 while any(p.poll() is None for _,p in processes):
  if late is None and (R/'ready-late.signal').exists() and client.poll() is not None:late=start('late')
  if any(p.poll() not in [None,0] for _,p in processes):break
  if time.monotonic()-begin>180:raise RuntimeError('Timeout')
  time.sleep(1)
 for role,p in processes:
  print(role,'exit',p.poll(),flush=True);report=R/(role+'.txt');print(report.read_text() if report.exists() else 'No report',flush=True)
 assert late is not None and all(p.poll()==0 for _,p in processes)
finally:
 for _,p in processes:
  if p.poll() is None:p.terminate()
 print('Results:',R,flush=True)
