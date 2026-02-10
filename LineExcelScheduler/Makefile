IMAGE_NAME=ghcr.io/thairaphat/lineexcelscheduler
TAG_NAME=$(shell date +%Y%m%d 2>/dev/null || powershell -Command "Get-Date -Format yyyyMMdd")
PORT=8080

build:
	docker build -t $(IMAGE_NAME):$(TAG_NAME) .

run:
	docker run -p $(PORT):$(PORT) $(IMAGE_NAME):$(TAG_NAME)

stop:
	docker stop $$(docker ps -q --filter ancestor=$(IMAGE_NAME):$(TAG_NAME))

clean:
	docker rmi $(IMAGE_NAME):$(TAG_NAME)

rebuild: clean build

push:
	docker push $(IMAGE_NAME):$(TAG_NAME)

.PHONY: build run stop clean rebuild
