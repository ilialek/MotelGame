from pathlib import Path
import subprocess,time,datetime
P=next((q for q in Path(__file__).resolve().parents if (q/'Assets/Game').is_dir()),Path('/Users/ilalekomtsev/MotelGame'));R=P/'Documentation/OnlineMultiplayerM13'/('profiles-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir(parents=True)
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';processes=[]
try:
 for role in ['auto1','auto2']:
  p=subprocess.Popen([str(app),'-motel-online-smoke-test',role,'-motel-online-results',str(R),'-motel-online','-logFile',str(R/(role+'.log')),'-batchmode','-nographics'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));time.sleep(5)
 for role,p in processes:
  p.wait(timeout=120);print((R/(role+'.txt')).read_text(),flush=True);assert p.returncode==0 and (R/(role+'.txt')).read_text().startswith('PASS')
finally:
 for _,p in processes:
  if p.poll() is None:p.kill();p.wait()
 print(R,flush=True)
