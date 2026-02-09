IMAGE_NAME=lineexcelscheduler
PORT=8080

build:
	docker build -t $(IMAGE_NAME) .

run:
	docker run -p $(PORT):$(PORT) $(IMAGE_NAME)

stop:
	docker stop $$(docker ps -q --filter ancestor=$(IMAGE_NAME))

clean:
	docker rmi $(IMAGE_NAME)

rebuild: clean build

.PHONY: build run stop clean rebuild
