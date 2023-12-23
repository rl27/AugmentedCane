
# http://raymondl.pythonanywhere.com/submit?sample=9.32
# http://raymondl.pythonanywhere.com/submit?file=11-28_22-49-33&count=7
# http://raymondl.pythonanywhere.com/submit?params=3.5,2.5,1.5,0.5
# http://raymondl.pythonanywhere.com/retrieve

from flask import Flask, request
import json
import datetime
import os

app = Flask(__name__)

@app.route('/hello', methods=['GET'])
def hello():
    return "Hello"

@app.route('/', methods=['POST'])
def hello_world():
    curtime = str(datetime.datetime.now())
    date = curtime.split()[0]
    time = curtime.split()[1].split(':')

    filename = "{0}_{1}-{2}-{3}.json".format(date[5:], time[0], time[1], time[2][:2])
    with open(filename, 'w') as f:
        json.dump(request.get_json(), f)

    return 'Hello'

@app.route('/submit', methods=['GET'])
def submit():
    sample = request.args.get('sample')
    file = request.args.get('file')
    count = request.args.get('count')
    params = request.args.get('params')
    command = {}
    if sample is not None:
        command = {'sample': sample}
    elif file is not None and count is not None:
        command = {'file': file, 'count': count}
    elif params is not None:
        command = {'params': params.split(',')}

    with open('command.json', 'w') as f:
        json.dump(command, f)
    return command

@app.route('/retrieve', methods=['GET'])
def retrieve():
    command = ''
    with open('command.json', 'r+') as f:
        command = json.load(f)
        f.seek(0)
        json.dump({}, f)
        f.truncate()
    if command != {}:
        response = {}
        if 'sample' in command:
            response['sample'] = command['sample']
        elif 'file' in command and 'count' in command:
            file = command['file'] + '.json'
            count = command['count']
            if os.path.exists(file):
                with open(file, 'r') as f:
                    data = json.load(f)
                try:
                    count = int(count)
                except:
                    count = len(data['inputs'])
                response['inputs'] = data['inputs'][:count]
                response['outputs'] = data['outputs'][:count]
        elif 'params' in command:
            response['prms'] = command['params']
        return response
    return {}

@app.route('/append', methods=['POST'])
def append():
    newData = request.get_json()
    if not os.path.exists('data.json'):
        with open('data.json', 'w+') as f:
            data = {}
            data['inputs'] = [newData['inputs']]
            data['outputs'] = [newData['outputs']]
            data['means'] = [newData['means']]
            data['sigmas'] = [newData['sigmas']]
            json.dump(data, f)
    else:
        with open('data.json', 'r+') as f:
            data = json.load(f)
            data['inputs'].append(newData['inputs'])
            data['outputs'].append(newData['outputs'])
            data['means'].append(newData['means'])
            data['sigmas'].append(newData['sigmas'])
            f.seek(0)
            json.dump(data, f)
    return 'Hello'
