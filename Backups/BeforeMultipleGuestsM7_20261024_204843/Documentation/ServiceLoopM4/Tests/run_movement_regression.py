from pathlib import Path
import subprocess,time,datetime
P=Path(__file__).resolve().parents[3];R=P/'Documentation/ServiceLoopM4'/('regression-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir();app=P/'Builds/ServiceLoopM4/MotelGame.app/Contents/MacOS/MotelGame';processes=[]
try:
 for role in ['host','client']:
  p=subprocess.Popen([str(app),'-batchmode','-nographics','-motel-smoke',role,'-motel-smoke-report',str(R/(role+'.txt')),'-logFile',str(R/(role+'.log'))],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started movement',role,p.pid,flush=True)
  if role=='host':time.sleep(3)
 end=time.monotonic()+120
 while any(p.poll() is None for _,p in processes):
  if time.monotonic()>end:raise RuntimeError('Regression timeout')
  time.sleep(1)
 for role,p in processes:print(role,p.returncode,(R/(role+'.txt')).read_text(),flush=True)
 assert all(p.returncode==0 for _,p in processes)
finally:
 for _,p in processes:
  if p.poll() is None:p.terminate()
 print('Results:',R,flush=True)
