BROKER_URL = 'sqla+sqlite:///celery_broker.sqlite' 
CELERY_RESULT_BACKEND = 'db+sqlite:///celery_results.sqlite'

CELERY_ACCEPT_CONTENT = ['json']
CELERY_TASK_SERIALIZER = 'json'
CELERY_RESULT_SERIALIZER = 'json'
CELERY_INCLUDE = ('app',)
